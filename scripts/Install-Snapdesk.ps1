# End-user Snapdesk installer (no prior build required if dist\Snapdesk exists).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Snapdesk.ps1
# Or double-click: Install Snapdesk.cmd

param(
    [switch] $AddToStartup
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms | Out-Null

function Show-Message {
    param(
        [string] $Text,
        [string] $Caption = "Snapdesk Setup",
        [System.Windows.Forms.MessageBoxButtons] $Buttons = [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon] $Icon = [System.Windows.Forms.MessageBoxIcon]::Information
    )
    [void][System.Windows.Forms.MessageBox]::Show($Text, $Caption, $Buttons, $Icon)
}

function Write-Step([string] $Message) {
    Write-Host $Message -ForegroundColor Cyan
}

function Test-IsWindowsAppsShim([string] $Path) {
    return [string]::IsNullOrWhiteSpace($Path) -or ($Path -match "\\WindowsApps\\")
}

function Find-PythonExe {
    $roots = @(
        "$env:LocalAppData\Programs\Python",
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    )
    foreach ($r in $roots) {
        if (-not $r -or -not (Test-Path -LiteralPath $r)) { continue }
        $dirs = Get-ChildItem -Path $r -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^Python' } |
            Sort-Object Name -Descending
        foreach ($d in $dirs) {
            foreach ($name in @("python.exe", "pythonw.exe")) {
                $p = Join-Path $d.FullName $name
                if ((Test-Path -LiteralPath $p) -and -not (Test-IsWindowsAppsShim $p)) {
                    try {
                        & $p -c "import sys; print(sys.executable)" 2>$null | Out-Null
                        if ($LASTEXITCODE -eq 0) { return $p }
                    }
                    catch { }
                }
            }
        }
    }
    try {
        $w = & where.exe python 2>$null | Where-Object { $_ -notmatch "\\WindowsApps\\" } | Select-Object -First 1
        if ($w -and (Test-Path -LiteralPath $w.Trim())) {
            $p = $w.Trim()
            & $p -c "import sys" 2>$null | Out-Null
            if ($LASTEXITCODE -eq 0) { return $p }
        }
    }
    catch { }
    return $null
}

function Invoke-WingetInstall {
    param(
        [string] $PackageId,
        [string] $Label
    )
    $winget = $null
    try { $winget = (& where.exe winget 2>$null | Select-Object -First 1) }
    catch { }
    if (-not $winget) {
        return @{ Ok = $false; Message = "winget not found on PATH." }
    }
    Write-Step "Installing $Label ($PackageId) via winget..."
    & winget install -e --id $PackageId --accept-package-agreements --accept-source-agreements --disable-interactivity
    $code = $LASTEXITCODE
    if ($code -eq 0 -or $code -eq -1978335189) {
        return @{ Ok = $true; Message = "winget exit $code" }
    }
    return @{
        Ok      = $false
        Message = "winget install failed (exit $code). You may need to approve the install in the Microsoft Store / winget UI, then run this installer again."
    }
}

function Find-SnapdeskExecutable([string] $Dir) {
    if (-not (Test-Path -LiteralPath $Dir)) { return $null }
    foreach ($name in @("Snapdesk.exe", "LayoutProfiles.WinUI.exe")) {
        $p = Join-Path $Dir $name
        if (Test-Path -LiteralPath $p) { return $p }
    }
    $candidates = Get-ChildItem -LiteralPath $Dir -Filter "*.exe" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notmatch "^(createdump|RestartAgent|vcruntime)" } |
        Sort-Object Length -Descending
    if ($candidates.Count -gt 0) { return $candidates[0].FullName }
    $dll = Join-Path $Dir "Snapdesk.dll"
    if (-not (Test-Path -LiteralPath $dll)) {
        $dll = Join-Path $Dir "LayoutProfiles.WinUI.dll"
    }
    if (Test-Path -LiteralPath $dll) { return $dll }
    return $null
}

function Test-SelfContainedPublish([string] $Dir) {
    $rt = Join-Path $Dir "Snapdesk.runtimeconfig.json"
    if (-not (Test-Path -LiteralPath $rt)) {
        $rt = Join-Path $Dir "LayoutProfiles.WinUI.runtimeconfig.json"
    }
    if (-not (Test-Path -LiteralPath $rt)) { return $false }
    try {
        $json = Get-Content -LiteralPath $rt -Raw | ConvertFrom-Json
        return [bool]$json.runtimeOptions.selfContained
    }
    catch {
        return $false
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$distDir = Join-Path $repoRoot "dist\Snapdesk"
$installDir = Join-Path $env:LOCALAPPDATA "Programs\Snapdesk"

Write-Step "Snapdesk installer"
Write-Step "Install location: $installDir"
Show-Message "Snapdesk will be installed to:`n`n$installDir`n`nClick OK to continue." "Snapdesk Setup"

# --- Dependencies ---
$wingetWarnings = New-Object System.Collections.Generic.List[string]

$payloadForDeps = if (Test-Path -LiteralPath $distDir) { $distDir } else { $installDir }
$selfContained = Test-SelfContainedPublish $payloadForDeps

if ($selfContained) {
    Write-Step "App publish is self-contained - skipping .NET 8 desktop runtime."
}
else {
    $r = Invoke-WingetInstall "Microsoft.DotNet.DesktopRuntime.8" ".NET 8 Desktop Runtime"
    if (-not $r.Ok) { $wingetWarnings.Add(".NET 8 Desktop Runtime: $($r.Message)") }
}

$r = Invoke-WingetInstall "Microsoft.WindowsAppRuntime.1.6" "Windows App Runtime 1.6 (x64)"
if (-not $r.Ok) {
    if ($selfContained) {
        Write-Step "Windows App Runtime winget step skipped/warned (app bundles WinApp SDK)."
    }
    else {
        $wingetWarnings.Add("Windows App Runtime: $($r.Message)")
    }
}

$py = Find-PythonExe
if (-not $py) {
    $r = Invoke-WingetInstall "Python.Python.3.12" "Python 3.12"
    if (-not $r.Ok) { $wingetWarnings.Add("Python 3.12: $($r.Message)") }
    Start-Sleep -Seconds 3
    $py = Find-PythonExe
}

if (-not $py) {
    Show-Message @"
Python was not found and could not be installed automatically.

Install Python 3.12 manually (tick Add python.exe to PATH), then run this installer again:
  winget install -e --id Python.Python.3.12

Turn OFF App Execution Aliases for python.exe in Windows Settings > Apps.
"@ "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Warning)
    exit 1
}

Write-Step "Using Python: $py"
Write-Step "Installing pywin32..."
& $py -m pip install --upgrade pip 2>&1 | Out-Host
& $py -m pip install pywin32
if ($LASTEXITCODE -ne 0) {
    Show-Message "pip install pywin32 failed. Try running as your normal user after fixing Python, or run:`n& '$py' -m pip install pywin32" "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Warning)
    exit 1
}

# --- Copy application files ---
$alreadyInstalled = (Test-Path -LiteralPath (Join-Path $installDir "invoke_python.ps1")) -and
    (Test-Path -LiteralPath (Join-Path $installDir "src\layout_manager.py")) -and
    ((Test-Path -LiteralPath (Join-Path $installDir "Snapdesk.exe")) -or
     (Test-Path -LiteralPath (Join-Path $installDir "LayoutProfiles.WinUI.exe")))

$winUiProj = Join-Path $repoRoot "LayoutProfiles.WinUI\LayoutProfiles.WinUI.csproj"
if (-not (Test-Path -LiteralPath $distDir) -and (Test-Path -LiteralPath $winUiProj)) {
    if (Get-Command dotnet -ErrorAction SilentlyContinue) {
        Write-Step "dist\Snapdesk not found - building app (first-time publish, may take a few minutes)..."
        Show-Message "Snapdesk will now compile the app. This can take several minutes on first run.`n`nClick OK to continue." "Snapdesk Setup"
        $publishScript = Join-Path $PSScriptRoot "publish_snapdesk.ps1"
        & powershell -NoProfile -ExecutionPolicy Bypass -File $publishScript
        if ($LASTEXITCODE -ne 0) {
            Show-Message "Building Snapdesk failed. Open PowerShell in the project folder and run:`n`n  scripts\publish_snapdesk.ps1`n`nThen run Install Snapdesk.cmd again." "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Error)
            exit 1
        }
    }
}

if (Test-Path -LiteralPath $distDir) {
    $distExe = Find-SnapdeskExecutable $distDir
    if (-not $distExe) {
        $distListed = (Get-ChildItem -LiteralPath $distDir -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join "`n"
        Show-Message @"
dist\Snapdesk exists but contains no Snapdesk executable (only support files).

Contents:
$distListed

From the project folder, run:
  powershell -ExecutionPolicy Bypass -File scripts\publish_snapdesk.ps1

Wait until you see ""Published executable: Snapdesk.exe"", then run Install Snapdesk.cmd again.

Or skip the installer and use the dev launcher:
  build_winui.cmd
  run_winui.cmd
"@ "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Error)
        exit 1
    }
    Write-Step "Copying published app from dist\Snapdesk (found $($distExe | Split-Path -Leaf))..."
    if (Test-Path -LiteralPath $installDir) {
        Remove-Item -LiteralPath $installDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    Copy-Item -Path (Join-Path $distDir "*") -Destination $installDir -Recurse -Force
}
elseif ($alreadyInstalled) {
    Write-Step "No dist\Snapdesk next to installer - keeping existing files in $installDir"
}
else {
    Show-Message @"
Snapdesk application files were not found.

For developers: run scripts\publish_snapdesk.ps1 first, or use scripts\Build-Installer.ps1 to create a setup zip.

For end users: extract the full Snapdesk-Setup.zip (with dist\Snapdesk) and run Install Snapdesk.cmd again.
"@ "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}

# Ensure launchers exist in install dir.
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
'@ | Set-Content -LiteralPath (Join-Path $installDir "Snapdesk.cmd") -Encoding ASCII

# --- Shortcuts ---
$iconPath = Join-Path $installDir "assets\app_icon.ico"
$exePath = Find-SnapdeskExecutable $installDir
if (-not $exePath) {
    $listed = (Get-ChildItem -LiteralPath $installDir -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name) -join "`n"
    Show-Message @"
Install finished but Snapdesk.exe was not found in:
$installDir

Folder contents:
$listed

Try running scripts\publish_snapdesk.ps1 again, then Install Snapdesk.cmd.
"@ "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}

$desktopDir = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktopDir)) {
    $desktopDir = Join-Path $env:USERPROFILE "Desktop"
}
$desktopLnk = Join-Path $desktopDir "Snapdesk.lnk"
$launcherVbs = Join-Path $installDir "launch_snapdesk.vbs"
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($desktopLnk)
if (Test-Path -LiteralPath $launcherVbs) {
    $shortcut.TargetPath = $launcherVbs
    $shortcut.WorkingDirectory = $installDir
}
else {
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $installDir
}
$shortcut.Description = 'Snapdesk - saved window layouts.'
if (Test-Path -LiteralPath $iconPath) {
    $shortcut.IconLocation = "$iconPath,0"
}
$shortcut.Save()
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null

$legacyShortcut = Join-Path $env:USERPROFILE "Desktop\Layout Profiles.lnk"
if (Test-Path -LiteralPath $legacyShortcut) {
    Remove-Item -LiteralPath $legacyShortcut -Force -ErrorAction SilentlyContinue
}

# --- Optional startup ---
$startupDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$widgetBat = Join-Path $startupDir "SnapdeskWidget.bat"
if ($AddToStartup) {
    New-Item -ItemType Directory -Path $startupDir -Force | Out-Null
    $widgetContent = @(
        '@echo off',
        'setlocal',
        "cd /d `"$installDir`"",
        "start `"`" `"$exePath`"",
        'exit /b 0'
    ) -join "`r`n"
    Set-Content -LiteralPath $widgetBat -Value $widgetContent -Encoding ASCII
    Write-Step "Added startup shortcut: $widgetBat"
}
elseif (Test-Path -LiteralPath $widgetBat) {
    Remove-Item -LiteralPath $widgetBat -Force -ErrorAction SilentlyContinue
}

# --- Done ---
$summary = @"
Snapdesk is installed.

Location:
  $installDir

Desktop shortcut:
  $desktopLnk

Start the app from the desktop icon or run:
  $installDir\Snapdesk.cmd
"@

if ($wingetWarnings.Count -gt 0) {
    $summary += "`n`nSome optional components may need manual install (open winget or Microsoft Store if prompted):`n- " + ($wingetWarnings -join "`n- ")
}

Show-Message $summary "Snapdesk Setup"
Write-Host $summary -ForegroundColor Green
exit 0
