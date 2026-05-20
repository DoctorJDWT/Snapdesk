@echo off
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\Install-Snapdesk.ps1"
if errorlevel 1 pause
