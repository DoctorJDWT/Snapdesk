@echo off
cd /d "%~dp0"
call "%~dp0launchers\install_desktop_shortcut.cmd" %*
exit /b %ERRORLEVEL%
