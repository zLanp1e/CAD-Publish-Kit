param([string]$SolidWorksDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (!$SolidWorksDirectory) {
    $clsid = (Get-Item 'Registry::HKEY_CLASSES_ROOT\SldWorks.Application\CLSID').GetValue('')
    $server = (Get-Item "Registry::HKEY_CLASSES_ROOT\CLSID\$clsid\LocalServer32").GetValue('')
    $exe = [regex]::Match($server, '(?i)^"?(.+?\.exe)').Groups[1].Value
    $SolidWorksDirectory = Split-Path $exe -Parent
}
$interop = Join-Path $SolidWorksDirectory 'api\redist'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
$references = @('SolidWorks.Interop.sldworks.dll', 'SolidWorks.Interop.swconst.dll')
foreach ($name in $references) {
    if (!(Test-Path (Join-Path $interop $name))) { throw "Missing local SolidWorks interface library: $name" }
    Copy-Item -LiteralPath (Join-Path $interop $name) -Destination $bin
}
$source = @(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /target:exe /main:CadPublishKit.Program /platform:x64 /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$bin\SolidWorks.Interop.sldworks.dll" "/r:$bin\SolidWorks.Interop.swconst.dll" "/out:$bin\CadPublishKit.exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
& $compiler /nologo /target:winexe /main:CadPublishKit.DesktopProgram /platform:x64 /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$bin\SolidWorks.Interop.sldworks.dll" "/r:$bin\SolidWorks.Interop.swconst.dll" "/out:$bin\CADPublishKit.Desktop.exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Desktop compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 /r:System.Web.Extensions.dll "/out:$bin\CoreTests.exe" "$root\src\Models.cs" "$root\src\ManifestWriter.cs" "$root\tests\CoreTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 "/r:$bin\CadPublishKit.exe" "/out:$bin\DesktopPublishTests.exe" "$root\tests\DesktopPublishTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Desktop test compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 "/r:$bin\CadPublishKit.exe" "/out:$bin\BatchTests.exe" "$root\tests\BatchTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Batch test compilation failed.' }
& $compiler /nologo /target:exe /platform:x64 "/r:$bin\CadPublishKit.exe" "/out:$bin\BatchCadTests.exe" "$root\tests\BatchCadTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Batch CAD test compilation failed.' }
Write-Host "Build complete: $bin"
