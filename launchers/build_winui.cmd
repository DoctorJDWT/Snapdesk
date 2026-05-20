@echo off
REM Build WinUI (always x64). Use scripts\build_winui.ps1 for a detailed build log.
cd /d "%~dp0\.."
echo Building LayoutProfiles.WinUI (Release, x64)...
dotnet build "LayoutProfiles.WinUI\LayoutProfiles.WinUI.csproj" -c Release -p:Platform=x64
if errorlevel 1 (
  echo.
  echo Build FAILED.
  exit /b 1
)
echo.
echo Build succeeded.
exit /b 0
