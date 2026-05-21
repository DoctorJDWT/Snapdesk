# Removes Snapdesk from the machine. Invoked detached after the app exits.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Uninstall-Snapdesk.ps1 -InstallDir "..." -ProcessId 1234

param(
    [Parameter(Mandatory = $true)]
    [string] $InstallDir,
    [Alias('WaitPid', 'WaitProcessId')]
    [int] $ProcessId = 0
)

$ErrorActionPreference = 'SilentlyContinue'

function Stop-SnapdeskProcesses {
    param(
        [string] $InstallDir,
        [int] $WaitProcessId = 0
    )

    foreach ($image in @('Snapdesk.exe', 'LayoutProfiles.WinUI.exe')) {
        & taskkill.exe /F /IM $image /T 2>$null | Out-Null
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallDir)) {
        $root = [System.IO.Path]::GetFullPath($InstallDir.TrimEnd('\', '/'))
        $rootPrefix = $root + [System.IO.Path]::DirectorySeparatorChar

        Get-Process -ErrorAction SilentlyContinue | Where-Object {
            $exePath = $_.Path
            if ([string]::IsNullOrWhiteSpace($exePath)) {
                return $false
            }
            try {
                $full = [System.IO.Path]::GetFullPath($exePath)
                return $full.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                    [string]::Equals($full, $root, [System.StringComparison]::OrdinalIgnoreCase)
            }
            catch {
                return $false
            }
        } | ForEach-Object {
            Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
        }

        try {
            Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue |
                Where-Object {
                    $exePath = $_.ExecutablePath
                    if ([string]::IsNullOrWhiteSpace($exePath)) {
                        return $false
                    }
                    return $exePath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
                        [string]::Equals($exePath, $root, [System.StringComparison]::OrdinalIgnoreCase)
                } |
                ForEach-Object {
                    & taskkill.exe /F /PID $_.ProcessId /T 2>$null | Out-Null
                }
        }
        catch {
            # WMI unavailable on some systems
        }
    }

    if ($WaitProcessId -gt 0) {
        $deadline = (Get-Date).AddSeconds(30)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Id $WaitProcessId -ErrorAction SilentlyContinue)) {
                break
            }
            Start-Sleep -Milliseconds 250
        }
        & taskkill.exe /F /PID $WaitProcessId /T 2>$null | Out-Null
    }

    Start-Sleep -Milliseconds 600
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

$startupDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'
Remove-FileRobust (Join-Path $startupDir 'SnapdeskWidget.bat')
Remove-FileRobust (Join-Path $startupDir 'LayoutProfilesWidget.bat')

Stop-SnapdeskProcesses -InstallDir $installDir -WaitProcessId $ProcessId

$desktopDir = [Environment]::GetFolderPath('Desktop')
if ([string]::IsNullOrWhiteSpace($desktopDir)) {
    $desktopDir = Join-Path $env:USERPROFILE 'Desktop'
}

Remove-FileRobust (Join-Path $desktopDir 'Snapdesk.lnk')
Remove-FileRobust (Join-Path $desktopDir 'Layout Profiles.lnk')

$dataDir = Join-Path $env:LOCALAPPDATA 'LayoutProfiles'
Remove-PathRobust $dataDir
Remove-PathRobust $installDir

exit 0
