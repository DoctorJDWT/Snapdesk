@echo off
cd /d "%~dp0"
call "%~dp0launchers\layout.cmd" %*
exit /b %ERRORLEVEL%
