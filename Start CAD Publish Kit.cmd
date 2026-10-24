@echo off
cd /d "%~dp0"
if not exist "%~dp0bin\SolidWorks.Interop.sldworks.dll" goto setup
if not exist "%~dp0bin\SolidWorks.Interop.swconst.dll" goto setup
goto launch
:setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\prepare-runtime.ps1"
if errorlevel 1 (
    pause
    exit /b 1
)
:launch
if exist "%~dp0bin\CADPublishKit.Desktop.exe" (
    start "" "%~dp0bin\CADPublishKit.Desktop.exe"
) else (
    echo Please build the project first. See README.md for instructions.
    pause
)
