@echo off
setlocal
cd /d "%~dp0\.."

where python >nul 2>&1
if errorlevel 1 (
  echo Python not found. Install Python 3 and ensure it is on PATH, then run this script again.
  exit /b 1
)

python "%CD%\scripts\build_app_assets.py"
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%CD%\scripts\install_desktop_shortcut.ps1"
if errorlevel 1 exit /b 1

echo.
echo Desktop shortcut installed: Snapdesk
echo Re-run this script anytime to refresh the desktop icon name.
echo.
pause
