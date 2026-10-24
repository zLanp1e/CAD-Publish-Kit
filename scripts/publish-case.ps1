param(
    [Parameter(Mandatory=$true)][string]$CaseDirectory,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
$caseRoot = (Resolve-Path -LiteralPath $CaseDirectory).Path
$reportFile = (Resolve-Path -LiteralPath $ReportPath).Path
$report = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
$destination = Join-Path $caseRoot 'parts'
$bomPath = Join-Path $caseRoot 'BOM.csv'
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $bomPath)) {
    throw 'BOM.csv or parts already exists. Preserve or move the existing delivery before publishing another.'
}
if (!$report.bodies -or !$report.source_state_preserved) { throw 'Missing body results or source-state check failed.' }
$sourceFiles = @(Get-ChildItem -LiteralPath $caseRoot -File | Where-Object Extension -in @('.step','.stp'))
if ($sourceFiles.Count -ne 1) { throw 'Expected exactly one source STEP beside BOM.csv.' }
$rows = @()
$names = @{}
foreach ($body in $report.bodies) {
    if ($body.status -notin @('verified','verified_with_save_warnings')) { throw "Body is not verified: $($body.body_id)" }
    if (!(Test-Path -LiteralPath $body.export_path -PathType Leaf)) { throw "Missing STEP: $($body.export_path)" }
    $name = [IO.Path]::GetFileName($body.export_path)
    if ($names.ContainsKey($name)) { throw "Duplicate STEP filename: $name" }
    $names[$name] = $true
    # Protect text fields when CSV is opened in Excel; retain raw names in the internal JSON.
    $bodyName = [string]$body.body_name
    if ($bodyName -match '^\s*[=+@-]') { $bodyName = "'" + $bodyName }
    $sourceName = $sourceFiles[0].Name
    if ($sourceName -match '^\s*[=+@-]') { $sourceName = "'" + $sourceName }
    $rows += [pscustomobject][ordered]@{
        Item = $rows.Count + 1
        BodyName = $bodyName
        BodyQuantity = 1
        STEPFile = 'parts/' + $name
        ExportStatus = $body.status
        OriginalFaces = $body.original_faces
        ReopenedFaces = $body.reopened_faces
        MaxAxisDeviation_mm = [double]$body.max_axis_delta * 1000
        SourceSTEP = $sourceName
        Notes = 'One exported solid body; not a reconstructed mechanical-parts BOM.'
    }
}
New-Item -ItemType Directory -Path $destination | Out-Null
foreach ($body in $report.bodies) {
    $target = Join-Path $destination ([IO.Path]::GetFileName($body.export_path))
    Copy-Item -LiteralPath $body.export_path -Destination $target
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $body.export_path).Hash) { throw 'STEP copy verification failed.' }
}
$csv = ($rows | ConvertTo-Csv -NoTypeInformation) -join "`r`n"
$stream = [IO.File]::Open($bomPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
try {
    $writer = [IO.StreamWriter]::new($stream,[Text.UTF8Encoding]::new($true))
    try { $writer.Write($csv + "`r`n") } finally { $writer.Dispose() }
} finally { $stream.Dispose() }
$check = @(Import-Csv -LiteralPath $bomPath)
if ($check.Count -ne $report.bodies.Count) { throw 'BOM row count mismatch.' }
foreach ($row in $check) { if (!(Test-Path -LiteralPath (Join-Path $caseRoot $row.STEPFile))) { throw 'BOM points to missing file.' } }
Write-Host "Delivery ready: $caseRoot"
