param(
    [string]$CaseName = 'MBNW02RZ-A-1-M1',
    [switch]$ExportBodies,
    [string]$Template
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($CaseName) -or $CaseName -in @('.', '..') -or $CaseName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw 'CaseName must be one directory name.'
}
$caseRoot = Join-Path (Join-Path $root 'test-cases') $CaseName
if (!(Test-Path -LiteralPath $caseRoot -PathType Container)) {
    throw "Test case is missing: $caseRoot"
}
$application = Join-Path $root 'bin\CadPublishKit.exe'
if (!(Test-Path -LiteralPath $application)) { throw 'Build the application with scripts/build.ps1 first.' }
$outputPath = Join-Path (Join-Path $root 'output') $CaseName
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$before = @(Get-ChildItem -LiteralPath $outputPath -Directory | ForEach-Object FullName)
$arguments = @($outputPath)
if ($ExportBodies) {
    if ((Test-Path -LiteralPath (Join-Path $caseRoot 'BOM.csv')) -or (Test-Path -LiteralPath (Join-Path $caseRoot 'parts'))) {
        throw 'This case already has a delivery. Preserve or move BOM.csv and parts before exporting again.'
    }
    $arguments = @('--export-bodies', $outputPath)
    if ($Template) { $arguments += @('--template', $Template) }
}
Write-Host "Results: $outputPath"
Write-Host 'The program processes the ACTIVE SolidWorks model. Open the intended case before running.'
& $application @arguments
$result = $LASTEXITCODE
if ($ExportBodies -and $result -eq 0) {
    $runs = @(Get-ChildItem -LiteralPath $outputPath -Directory | Where-Object FullName -notin $before)
    if ($runs.Count -ne 1) { throw 'Cannot identify the completed export run.' }
    & (Join-Path $PSScriptRoot 'publish-case.ps1') -CaseDirectory $caseRoot -ReportPath (Join-Path $runs[0].FullName 'export_report.json')
}
exit $result
