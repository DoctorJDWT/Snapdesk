# Creates a desktop shortcut for Snapdesk (WinUI).
# Invoked by install_desktop_shortcut.cmd at repo root.

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$shortcutPath = Join-Path $env:USERPROFILE "Desktop\Snapdesk.lnk"

$outDir = Join-Path $repoRoot "LayoutProfiles.WinUI\bin\x64\Release\net8.0-windows10.0.19041.0"
$exePath = Join-Path $outDir "Snapdesk.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    $exePath = Join-Path $outDir "LayoutProfiles.WinUI.exe"
}
$runCmd = Join-Path $repoRoot "Open Snapdesk.cmd"
if (-not (Test-Path -LiteralPath $runCmd)) {
    $runCmd = Join-Path $repoRoot "run_winui.cmd"
}
$iconPath = (Resolve-Path (Join-Path $repoRoot "assets\app_icon.ico")).Path

if (Test-Path -LiteralPath $exePath) {
    $targetPath = $exePath
} elseif (Test-Path -LiteralPath $runCmd) {
    $targetPath = $runCmd
} else {
    throw "No launcher found. Build the app or ensure run_winui.cmd exists under: $repoRoot"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $targetPath
$shortcut.WorkingDirectory = $repoRoot
$shortcut.Description = "Snapdesk — Saved window layouts. Snap your apps back into place."
$shortcut.IconLocation = "$iconPath,0"
$shortcut.Save()

[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null

# Remove legacy shortcut name if present.
$legacyShortcut = Join-Path $env:USERPROFILE "Desktop\Layout Profiles.lnk"
if (Test-Path -LiteralPath $legacyShortcut) {
    Remove-Item -LiteralPath $legacyShortcut -Force
}

Write-Host "Created: $shortcutPath" -ForegroundColor Green
