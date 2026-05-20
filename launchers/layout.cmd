@echo off
setlocal
cd /d "%~dp0\.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%CD%\invoke_python.ps1" src\layout_manager.py %*
exit /b %ERRORLEVEL%
