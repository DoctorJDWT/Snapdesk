# Removes Snapdesk from the machine. Invoked detached after the app exits.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Uninstall-Snapdesk.ps1 -InstallDir "..." -WaitProcessId 1234

param(
    [Parameter(Mandatory = $true)]
    [string] $InstallDir,
    [Alias('WaitPid')]
    [int] $WaitProcessId = 0
)

$ErrorActionPreference = 'SilentlyContinue'

function Wait-ProcessExit([int] $ProcessId) {
    if ($ProcessId -le 0) {
        return
    }

    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
        if ($proc) {
            $null = $proc.WaitForExit(120000)
        }
    }
    catch {
        # already exited
    }

    Start-Sleep -Seconds 1
}

function Stop-SnapdeskProcesses {
    foreach ($name in @('Snapdesk', 'LayoutProfiles.WinUI')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue |
            Stop-Process -Force -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 500
}

function Remove-FileRobust([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($i = 0; $i -lt 8; $i++) {
        try {
            Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
            return
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }
}

function Remove-PathRobust([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return
    }

    for ($i = 0; $i -lt 12; $i++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            Start-Sleep -Milliseconds 750
        }
    }
}

$installDir = [System.IO.Path]::GetFullPath($InstallDir.Trim())
Wait-ProcessExit -ProcessId $WaitProcessId
Stop-SnapdeskProcesses

$desktopDir = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktopDir)) {
    $desktopDir = Join-Path $env:USERPROFILE 'Desktop'
}

Remove-FileRobust (Join-Path $desktopDir 'Snapdesk.lnk')
Remove-FileRobust (Join-Path $desktopDir 'Layout Profiles.lnk')

$startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
Remove-FileRobust (Join-Path $startupDir 'SnapdeskWidget.bat')
Remove-FileRobust (Join-Path $startupDir 'LayoutProfilesWidget.bat')

$dataDir = Join-Path $env:LOCALAPPDATA 'LayoutProfiles'
Remove-PathRobust $dataDir
Remove-PathRobust $installDir

exit 0
