@echo off
REM Snapdesk — build and launch WinUI widget
setlocal
cd /d "%~dp0\.."
set "ROOT=%CD%"
set "PROJ=%ROOT%\LayoutProfiles.WinUI\LayoutProfiles.WinUI.csproj"
set "OUT=%ROOT%\LayoutProfiles.WinUI\bin\x64\Release\net8.0-windows10.0.19041.0"
set "EXE=%OUT%\Snapdesk.exe"
set "EXE_LEGACY=%OUT%\LayoutProfiles.WinUI.exe"

where dotnet >nul 2>&1
if errorlevel 1 (
  echo dotnet SDK not found. Install .NET 8 SDK, then open a new terminal.
  echo   winget install Microsoft.DotNet.SDK.8
  exit /b 1
)

echo Building WinUI (x64 Release)...
dotnet build "%PROJ%" -c Release -p:Platform=x64
if errorlevel 1 (
  echo.
  echo Build FAILED. Fix errors above, then run this script again.
  exit /b 1
)

echo.
echo Starting app from:
echo   %OUT%
echo.

if exist "%LOCALAPPDATA%\Programs\Snapdesk\Snapdesk.exe" (
  echo NOTE: An installed copy exists at %LOCALAPPDATA%\Programs\Snapdesk\
  echo       This launcher runs the DEV build above, not the desktop shortcut install.
  echo       For latest dev bits, use Open Snapdesk.cmd — not the desktop shortcut.
  echo.
)

if exist "%EXE%" (
  for %%F in ("%EXE%") do echo Snapdesk.exe built: %%~tF
  echo Startup log: %LOCALAPPDATA%\LayoutProfiles\snapdesk-startup.log
  echo.
  pushd "%OUT%"
  start "" "%EXE%"
  popd
  echo Launched Snapdesk.exe
  exit /b 0
)

if exist "%EXE_LEGACY%" (
  pushd "%OUT%"
  start "" "%EXE_LEGACY%"
  popd
  echo Launched LayoutProfiles.WinUI.exe
  exit /b 0
)

if exist "%OUT%\Snapdesk.dll" (
  pushd "%OUT%"
  start "" dotnet exec "Snapdesk.dll"
  popd
  echo Launched via dotnet exec Snapdesk.dll
  exit /b 0
)

if exist "%OUT%\LayoutProfiles.WinUI.dll" (
  pushd "%OUT%"
  start "" dotnet exec "LayoutProfiles.WinUI.dll"
  popd
  echo Launched via dotnet exec LayoutProfiles.WinUI.dll
  exit /b 0
)

echo Build output folder exists but no Snapdesk.exe was found.
echo Run: dotnet build "%PROJ%" -c Release -p:Platform=x64
echo Then check for %OUT%\Snapdesk.exe
if exist "%OUT%" dir "%OUT%\*.exe" "%OUT%\*.dll" 2>nul
pause
exit /b 1
