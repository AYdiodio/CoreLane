@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
  echo Build failed. Please keep this window for troubleshooting.
  pause
  exit /b 1
)
echo Double-click Start.cmd to open CoreLane.
pause
