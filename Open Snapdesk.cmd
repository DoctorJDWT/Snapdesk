@echo off
title Open Snapdesk
cd /d "%~dp0"
echo === Snapdesk launcher ===
echo.

call "%~dp0launchers\run_winui.cmd"
set "RC=%ERRORLEVEL%"

if not "%RC%"=="0" (
  echo.
  echo Snapdesk did not start. Error level: %RC%
  echo.
  echo If you see a Windows App Runtime dialog, click Yes once.
  echo If build failed, fix errors above and try again.
  pause
  exit /b %RC%
)

echo.
echo If no window appeared, check the taskbar or run publish_snapdesk.ps1 for a full install.
timeout /t 3 >nul
exit /b 0
