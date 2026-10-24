param([string]$Version = '0.1.0-preview.1')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$root = Split-Path $PSScriptRoot -Parent
$releaseRoot = Join-Path $root ('output\releases\' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
$name = 'CAD-Publish-Kit-' + $Version + '-win-x64'
$stage = Join-Path $releaseRoot $name
New-Item -ItemType Directory -Path $stage | Out-Null
# Explicit allowlist: never package local CAD, reports, test fixtures or vendor DLLs.
$files = @('Start CAD Publish Kit.cmd', 'README.md', 'README.zh-CN.md', 'LICENSE',
    'THIRD-PARTY-NOTICES.md', 'docs\INSTALL.md', 'docs\RELEASE-NOTES.md',
    'scripts\prepare-runtime.ps1', 'bin\CADPublishKit.Desktop.exe', 'bin\CadPublishKit.exe')
foreach ($file in $files) {
    $source = Join-Path $root $file
    if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing release input: $file" }
    $target = Join-Path $stage $file
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target
}
$entries = foreach ($file in $files) {
    $hash = Get-FileHash -LiteralPath (Join-Path $stage $file) -Algorithm SHA256
    '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), $file.Replace('\','/')
}
$entries | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ascii
$zip = Join-Path $releaseRoot ($name + '.zip')
Compress-Archive -LiteralPath $stage -DestinationPath $zip
$hash = Get-FileHash -LiteralPath $zip -Algorithm SHA256
('{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $zip -Leaf)) |
    Set-Content -LiteralPath ($zip + '.sha256') -Encoding ascii
Write-Output $zip
