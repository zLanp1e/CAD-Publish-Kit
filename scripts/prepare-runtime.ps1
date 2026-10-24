param([string]$SolidWorksDirectory)
$ErrorActionPreference = 'Stop'
try {
    $root = Split-Path $PSScriptRoot -Parent
    if (![Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
    $framework = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
    if ($framework.Release -lt 528040) { throw '.NET Framework 4.8 or later is required.' }
    if (!$SolidWorksDirectory) {
        $clsid = (Get-Item 'Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID').GetValue('')
        $server = (Get-Item "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid\LocalServer32").GetValue('')
        $exe = [regex]::Match($server, '(?i)^"?(.+?\.exe)').Groups[1].Value
        $SolidWorksDirectory = Split-Path $exe -Parent
    }
    $bin = Join-Path $root 'bin'
    New-Item -ItemType Directory -Force -Path $bin | Out-Null
    foreach ($name in @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')) {
        $source = Join-Path $SolidWorksDirectory ('api\redist\' + $name)
        if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing local SolidWorks interface library: $source" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $bin $name) -Force
    }
    Write-Host 'Local SolidWorks runtime is ready.'
} catch {
    Write-Host ('Startup setup failed: ' + $_.Exception.Message)
    Write-Host 'See docs/INSTALL.md. No CAD source files were changed.'
    exit 1
}
