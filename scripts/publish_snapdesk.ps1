# Publish Snapdesk for distribution (maintainer / CI).
# From repo root: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish_snapdesk.ps1

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$proj = Join-Path $root "LayoutProfiles.WinUI\LayoutProfiles.WinUI.csproj"
$dist = Join-Path $root "dist\Snapdesk"

Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found. Install .NET 8 SDK and open a new PowerShell window."
}

# Regenerate app_icon.ico when Python is available.
$assetScript = Join-Path $root "scripts\build_app_assets.py"
if (Test-Path -LiteralPath $assetScript) {
    $py = $null
    foreach ($candidate in @("python", "py")) {
        if (Get-Command $candidate -ErrorAction SilentlyContinue) {
            try {
                if ($candidate -eq "py") {
                    & py -3 $assetScript
                }
                else {
                    & python $assetScript
                }
                if ($LASTEXITCODE -eq 0) { break }
            }
            catch {
                Write-Host "Asset build skipped ($candidate): $_" -ForegroundColor DarkYellow
            }
        }
    }
}

if (-not (Test-Path -LiteralPath (Join-Path $root "assets\app_icon.ico"))) {
    Write-Warning "assets\app_icon.ico missing. Run: python scripts\build_app_assets.py"
}

Write-Host "=== dotnet publish (Release, x64, self-contained) ===" -ForegroundColor Cyan
if (Test-Path -LiteralPath $dist) {
    Remove-Item -LiteralPath $dist -Recurse -Force
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

dotnet publish $proj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $dist
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$publishedExe = Get-ChildItem -LiteralPath $dist -Filter "*.exe" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match "^(Snapdesk|LayoutProfiles\.WinUI)\.exe$" } |
    Select-Object -First 1
if (-not $publishedExe) {
    Write-Error "dotnet publish succeeded but no Snapdesk.exe / LayoutProfiles.WinUI.exe in $dist"
}

Write-Host "=== Published executable: $($publishedExe.Name) ===" -ForegroundColor Green

Write-Host "=== Copying Snapdesk payload files ===" -ForegroundColor Cyan

Copy-Item -LiteralPath (Join-Path $root "invoke_python.ps1") -Destination $dist -Force

$scriptsDest = Join-Path $dist "scripts"
New-Item -ItemType Directory -Path $scriptsDest -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root "scripts\Uninstall-Snapdesk.ps1") -Destination $scriptsDest -Force
Copy-Item -LiteralPath (Join-Path $root "requirements.txt") -Destination $dist -Force

$srcDest = Join-Path $dist "src"
New-Item -ItemType Directory -Path $srcDest -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root "src\layout_manager.py") -Destination $srcDest -Force

$assetsDest = Join-Path $dist "assets"
if (Test-Path -LiteralPath (Join-Path $root "assets")) {
    Copy-Item -LiteralPath (Join-Path $root "assets") -Destination $assetsDest -Recurse -Force
}

# Friendly launcher name alongside the published assembly.
$snapdeskExe = Join-Path $dist "Snapdesk.exe"
if ($publishedExe.Name -ne "Snapdesk.exe") {
    Copy-Item -LiteralPath $publishedExe.FullName -Destination $snapdeskExe -Force
}

@'
@echo off
setlocal
cd /d "%~dp0"
if exist "%~dp0Snapdesk.exe" (
  start "" "%~dp0Snapdesk.exe"
  exit /b 0
)
if exist "%~dp0LayoutProfiles.WinUI.exe" (
  start "" "%~dp0LayoutProfiles.WinUI.exe"
  exit /b 0
)
echo Snapdesk executable not found in %~dp0
exit /b 1
'@ | Set-Content -LiteralPath (Join-Path $dist "Snapdesk.cmd") -Encoding ASCII

@'
' Launch Snapdesk without a console window (publish / install folder).
Option Explicit
Dim fso, dir, exePath
Set fso = CreateObject("Scripting.FileSystemObject")
dir = fso.GetParentFolderName(WScript.ScriptFullName)
exePath = fso.BuildPath(dir, "Snapdesk.exe")
If Not fso.FileExists(exePath) Then
  exePath = fso.BuildPath(dir, "LayoutProfiles.WinUI.exe")
End If
If fso.FileExists(exePath) Then
  CreateObject("Shell.Application").ShellExecute exePath, "", dir, "open", 1
Else
  CreateObject("WScript.Shell").Popup "Snapdesk executable not found in:" & vbCrLf & dir, 0, "Snapdesk", 16
  WScript.Quit 1
End If
'@ | Set-Content -LiteralPath (Join-Path $dist "launch_snapdesk.vbs") -Encoding ASCII

$pythonReadme = Join-Path $dist "python\README.txt"
New-Item -ItemType Directory -Path (Split-Path $pythonReadme) -Force | Out-Null
@'
Python is not bundled in this folder.

End-user setup (Install Snapdesk.cmd) installs Python 3.12 via winget when needed
and runs: pip install pywin32

Developers can use: scripts\install_python.ps1
'@ | Set-Content -LiteralPath $pythonReadme -Encoding UTF8

Write-Host ""
Write-Host "Published to: $dist" -ForegroundColor Green
Write-Host "Next: scripts\Build-Installer.ps1  or  scripts\Install-Snapdesk.ps1 (local install)"
exit 0
