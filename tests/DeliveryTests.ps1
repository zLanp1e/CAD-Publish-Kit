$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $root ('output\delivery-tests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$passed = 0
function New-Fixture([string]$Name) {
    $folder = Join-Path $testRoot $Name
    $case = Join-Path $folder 'case'
    $exports = Join-Path $folder 'exports'
    New-Item -ItemType Directory -Path $case,$exports | Out-Null
    Set-Content -LiteralPath (Join-Path $case 'source.step') -Value 'Synthetic source; not CAD geometry.'
    Set-Content -LiteralPath (Join-Path $exports 'body_001.step') -Value 'Synthetic output; not CAD geometry.'
    $report = [pscustomobject]@{
        source_state_preserved=$true
        bodies=@([pscustomobject]@{
            body_id='body_1'; body_name='=unsafe,"name'; status='verified'
            export_path=(Join-Path $exports 'body_001.step')
            original_faces=12; reopened_faces=12; max_axis_delta=0.0000005
        })
    }
    return @{case=$case; report=$report; path=(Join-Path $folder 'report.json')}
}
function Publish($fixture) {
    $fixture.report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $fixture.path -Encoding utf8
    & (Join-Path $root 'scripts\publish-case.ps1') -CaseDirectory $fixture.case -ReportPath $fixture.path
}
function Refuses($fixture,[string]$pattern) {
    $rejected=$false
    try { Publish $fixture } catch {
        if($_.Exception.Message -notlike $pattern){throw}
        $rejected=$true
    }
    if(!$rejected){throw 'Invalid delivery was accepted.'}
    if(Test-Path -LiteralPath (Join-Path $fixture.case 'BOM.csv')){throw 'Invalid delivery published a BOM.'}
}
$good=New-Fixture 'success'
Publish $good
$rows=@(Import-Csv -LiteralPath (Join-Path $good.case 'BOM.csv'))
if($rows.Count -ne 1 -or $rows[0].BodyName -ne "'=unsafe,`"name" -or [double]$rows[0].MaxAxisDeviation_mm -ne 0.0005){throw 'CSV quoting, formula protection or unit conversion failed.'}
$passed++
$hash=(Get-FileHash -LiteralPath (Join-Path $good.case 'BOM.csv')).Hash
$rejected=$false
try {Publish $good} catch {if($_.Exception.Message -notlike 'BOM.csv or parts already exists*'){throw}; $rejected=$true}
if(!$rejected -or (Get-FileHash -LiteralPath (Join-Path $good.case 'BOM.csv')).Hash -ne $hash){throw 'Overwrite guard failed.'}
$passed++
$bad=New-Fixture 'failed-body'; $bad.report.bodies[0].status='failed'; Refuses $bad 'Body is not verified*'; $passed++
$bad=New-Fixture 'missing-file'; $bad.report.bodies[0].export_path=Join-Path $testRoot 'missing.step'; Refuses $bad 'Missing STEP*'; $passed++
$bad=New-Fixture 'source-state'; $bad.report.source_state_preserved=$false; Refuses $bad 'Missing body results or source-state check failed*'; $passed++
$bad=New-Fixture 'duplicate-name'; $bad.report.bodies+= $bad.report.bodies[0]; Refuses $bad 'Duplicate STEP filename*'; $passed++
Write-Host "PASS: $passed delivery checks. Synthetic fixtures only; no CAD API calls."
Write-Host "Test records: $testRoot"
