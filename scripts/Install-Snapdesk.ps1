# End-user Snapdesk installer (no prior build required if dist\Snapdesk exists).
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Install-Snapdesk.ps1
# Or double-click: Install Snapdesk.cmd

param(
    [string] $InstallDir,
    [switch] $AddToStartup,
    [switch] $NoPrompt,
    [switch] $LaunchAfterInstall,
    [switch] $NoLaunch,
    [int] $WaitPid = 0
)

$ErrorActionPreference = "Stop"

# Launch when install finishes unless -NoLaunch. First-run Install Snapdesk.cmd needs no flags.
$shouldLaunchAfterInstall = -not $NoLaunch
if ($PSBoundParameters.ContainsKey('LaunchAfterInstall')) {
    $shouldLaunchAfterInstall = [bool]$LaunchAfterInstall
}

Add-Type -AssemblyName System.Windows.Forms | Out-Null

function Show-Message {
    param(
        [string] $Text,
        [string] $Caption = "Snapdesk Setup",
        [System.Windows.Forms.MessageBoxButtons] $Buttons = [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon] $Icon = [System.Windows.Forms.MessageBoxIcon]::Information
    )
    return [System.Windows.Forms.MessageBox]::Show($Text, $Caption, $Buttons, $Icon)
}

function Write-Step([string] $Message) {
    Write-Host $Message -ForegroundColor Cyan
}

function Wait-ProcessExit([int] $Pid) {
    if ($Pid -le 0) {
        return
    }

    try {
        $proc = Get-Process -Id $Pid -ErrorAction Stop
        if ($proc) {
            $null = $proc.WaitForExit(120000)
        }
    }
    catch {
        # already exited
    }

    Start-Sleep -Seconds 1
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

function Stop-SnapdeskProcessesInDirectory {
    param([string] $InstallDirectory)

    if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
        return 0
    }

    $root = [System.IO.Path]::GetFullPath($InstallDirectory.TrimEnd('\', '/'))
    $stopped = 0
    foreach ($proc in Get-Process -ErrorAction SilentlyContinue) {
        try {
            $exe = $proc.Path
            if ([string]::IsNullOrWhiteSpace($exe)) {
                continue
            }
            $exeRoot = [System.IO.Path]::GetFullPath((Split-Path $exe -Parent))
            if (-not $exeRoot.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }

            Write-Step "Closing $($proc.ProcessName) so files can be updated..."
            if ($proc.CloseMainWindow()) {
                if ($proc.WaitForExit(3000)) {
                    $stopped++
                    continue
                }
            }
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            $stopped++
        }
        catch {
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                $stopped++
            }
            catch {
                # ignore processes we cannot terminate
            }
        }
    }

    if ($stopped -gt 0) {
        Start-Sleep -Milliseconds 800
    }
    return $stopped
}

function Copy-SnapdeskPayload {
    param(
        [string] $SourceDir,
        [string] $TargetDir
    )

    $staging = Join-Path $env:TEMP ("Snapdesk-staging-" + [guid]::NewGuid().ToString("N"))
    $backupDir = "$TargetDir.old"
    New-Item -ItemType Directory -Path $staging -Force | Out-Null

    try {
        Copy-Item -Path (Join-Path $SourceDir "*") -Destination $staging -Recurse -Force

        $closed = Stop-SnapdeskProcessesInDirectory $TargetDir
        if ($closed -gt 0) {
            Show-Message "Snapdesk was closed so the installer could update files in:`n`n$TargetDir" "Snapdesk Setup"
        }

        if (Test-Path -LiteralPath $TargetDir) {
            if (Test-Path -LiteralPath $backupDir) {
                Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
            }
            try {
                Rename-Item -LiteralPath $TargetDir -NewName (Split-Path $backupDir -Leaf) -ErrorAction Stop
            }
            catch {
                Stop-SnapdeskProcessesInDirectory $TargetDir | Out-Null
                try {
                    Rename-Item -LiteralPath $TargetDir -NewName (Split-Path $backupDir -Leaf) -ErrorAction Stop
                }
                catch {
                    throw [System.IO.IOException]::new(
                        "Could not replace the existing install because files are still in use.`n`nClose Snapdesk (check the system tray and Task Manager), then run Install Snapdesk.cmd again.`n`n$($_.Exception.Message)")
                }
            }
        }

        $parent = Split-Path $TargetDir -Parent
        $leaf = Split-Path $TargetDir -Leaf
        Move-Item -LiteralPath $staging -Destination (Join-Path $parent $leaf) -Force
        $staging = $null

        if (Test-Path -LiteralPath $backupDir) {
            Remove-Item -LiteralPath $backupDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    finally {
        if ($staging -and (Test-Path -LiteralPath $staging)) {
            Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Start-InstalledSnapdesk([string] $Dir) {
    if ([string]::IsNullOrWhiteSpace($Dir) -or -not (Test-Path -LiteralPath $Dir)) {
        return $false
    }

    $launcherVbs = Join-Path $Dir "launch_snapdesk.vbs"
    if (Test-Path -LiteralPath $launcherVbs) {
        Start-Process -FilePath $launcherVbs -WorkingDirectory $Dir
        return $true
    }

    $exePath = Find-SnapdeskExecutable $Dir
    if ($exePath) {
        Start-Process -FilePath $exePath -WorkingDirectory $Dir
        return $true
    }

    $cmd = Join-Path $Dir "Snapdesk.cmd"
    if (Test-Path -LiteralPath $cmd) {
        Start-Process -FilePath $cmd -WorkingDirectory $Dir
        return $true
    }

    return $false
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

function Get-DefaultInstallDir {
    Join-Path $env:LOCALAPPDATA "Programs\Snapdesk"
}

function Test-SnapdeskInstallDir([string] $Dir) {
    if ([string]::IsNullOrWhiteSpace($Dir) -or -not (Test-Path -LiteralPath $Dir)) {
        return $false
    }
    $hasPayload = (Test-Path -LiteralPath (Join-Path $Dir "invoke_python.ps1")) -and
        (Test-Path -LiteralPath (Join-Path $Dir "src\layout_manager.py"))
    $hasExe = $null -ne (Find-SnapdeskExecutable $Dir)
    return $hasPayload -or $hasExe
}

function Get-InstalledSnapdeskDir {
    $desktopDir = [Environment]::GetFolderPath('Desktop')
    if ([string]::IsNullOrWhiteSpace($desktopDir)) {
        $desktopDir = Join-Path $env:USERPROFILE "Desktop"
    }
    $desktopLnk = Join-Path $desktopDir "Snapdesk.lnk"
    if (-not (Test-Path -LiteralPath $desktopLnk)) {
        return $null
    }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($desktopLnk)
        $dir = $shortcut.WorkingDirectory
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut) | Out-Null
        [System.Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
        if (Test-SnapdeskInstallDir $dir) {
            return $dir
        }
    }
    catch {
        return $null
    }
    return $null
}

function Resolve-InstallDirectory([string] $SelectedPath) {
    $path = [System.IO.Path]::GetFullPath($SelectedPath.Trim())
    if (Test-SnapdeskInstallDir $path) {
        return $path
    }
    if ([string]::Equals(
            [System.IO.Path]::GetFileName($path.TrimEnd('\', '/')),
            "Snapdesk",
            [System.StringComparison]::OrdinalIgnoreCase)) {
        return $path
    }
    return Join-Path $path "Snapdesk"
}

function Ensure-FolderDialogPath([string] $Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    $full = [System.IO.Path]::GetFullPath($Path.Trim())
    if (-not (Test-Path -LiteralPath $full)) {
        New-Item -ItemType Directory -Path $full -Force | Out-Null
    }
    return $full
}

function Ensure-InstallFolderPickerType {
    if (([System.Management.Automation.PSTypeName]'Snapdesk.InstallFolderPicker').Type) {
        return
    }
    Add-Type @'
using System;
using System.Runtime.InteropServices;

namespace Snapdesk
{
    [ComImport]
    [Guid("DC1C5A9C-E88A-4CDE-A161-0443A2F0C3EC")]
    internal class FileOpenDialog
    {
    }

    [Flags]
    internal enum FOS : uint
    {
        PickFolders = 0x20,
        ForceFileSystem = 0x40,
        PathMustExist = 0x800
    }

    internal enum SIGDN : uint
    {
        FileSystemPath = 0x80058000
    }

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        [PreserveSig]
        int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("d57ffd71-a236-11d0-8c0f-00a02400c00b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOpenDialog
    {
        [PreserveSig]
        int Show(IntPtr hwndOwner);
        void SetFileTypes();
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise();
        void Unadvise();
        void SetOptions(FOS fos);
        FOS GetOptions();
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter();
        void GetResults();
        void GetSelectedItems();
    }

    public static class InstallFolderPicker
    {
        private static readonly Guid ShellItemGuid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        public static string Show(string title, string initialFolder)
        {
            IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();
            dialog.SetOptions(FOS.PickFolders | FOS.ForceFileSystem | FOS.PathMustExist);
            if (!string.IsNullOrWhiteSpace(title))
            {
                dialog.SetTitle(title);
            }

            if (!string.IsNullOrWhiteSpace(initialFolder))
            {
                Guid riid = ShellItemGuid;
                IShellItem item;
                SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, ref riid, out item);
                dialog.SetFolder(item);
                dialog.SetDefaultFolder(item);
            }

            if (dialog.Show(IntPtr.Zero) != 0)
            {
                return null;
            }

            IShellItem result;
            dialog.GetResult(out result);
            IntPtr pszPath;
            result.GetDisplayName(SIGDN.FileSystemPath, out pszPath);
            try
            {
                return Marshal.PtrToStringUni(pszPath);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pszPath);
            }
        }
    }
}
'@
}

function Select-InstallDirectory([string] $SuggestedPath) {
    Ensure-InstallFolderPickerType
    $title = "Choose where to install Snapdesk. A Snapdesk folder is created inside the folder you pick unless you select an existing Snapdesk install folder."
    $initialPath = if ([string]::IsNullOrWhiteSpace($SuggestedPath)) {
        Get-DefaultInstallDir
    }
    else {
        Resolve-InstallDirectory $SuggestedPath
    }
    $initialPath = Ensure-FolderDialogPath $initialPath
    if (-not $initialPath) {
        $initialPath = $env:USERPROFILE
    }
    $picked = [Snapdesk.InstallFolderPicker]::Show($title, $initialPath)
    if ([string]::IsNullOrWhiteSpace($picked)) {
        return $null
    }
    return Resolve-InstallDirectory $picked
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
$defaultInstallDir = Get-DefaultInstallDir
$existingInstallDir = Get-InstalledSnapdeskDir

if (-not [string]::IsNullOrWhiteSpace($InstallDir)) {
    $installDir = Resolve-InstallDirectory $InstallDir
}
elseif ($NoPrompt) {
    $installDir = if ($existingInstallDir) { $existingInstallDir } else { $defaultInstallDir }
}
else {
    $suggested = if ($existingInstallDir) { $existingInstallDir } else { $defaultInstallDir }
    $picked = Select-InstallDirectory $suggested
    if (-not $picked) {
        Show-Message "Installation cancelled." "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Information)
        exit 0
    }
    $installDir = $picked
}

Wait-ProcessExit -Pid $WaitPid

Write-Step "Snapdesk installer"
Write-Step "Install location: $installDir"
if (-not $NoPrompt -and [string]::IsNullOrWhiteSpace($InstallDir)) {
    $confirm = Show-Message "Install Snapdesk to:`n`n$installDir`n`nClick Yes to continue or No to pick a different folder." "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::YesNo) ([System.Windows.Forms.MessageBoxIcon]::Question)
    if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) {
        $picked = Select-InstallDirectory $installDir
        if (-not $picked) {
            Show-Message "Installation cancelled." "Snapdesk Setup"
            exit 0
        }
        $installDir = $picked
        Write-Step "Install location: $installDir"
    }
}

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
    New-Item -ItemType Directory -Path (Split-Path $installDir -Parent) -Force | Out-Null
    try {
        Copy-SnapdeskPayload -SourceDir $distDir -TargetDir $installDir
    }
    catch {
        Show-Message $_.Exception.Message "Snapdesk Setup" ([System.Windows.Forms.MessageBoxButtons]::OK) ([System.Windows.Forms.MessageBoxIcon]::Warning)
        exit 1
    }
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

if ($shouldLaunchAfterInstall) {
    Write-Step "Starting Snapdesk..."
    if (-not (Start-InstalledSnapdesk $installDir)) {
        Write-Host "Warning: install finished but Snapdesk could not be started from $installDir" -ForegroundColor Yellow
    }
}

if ($shouldLaunchAfterInstall -and $NoPrompt) {
    Write-Host $summary -ForegroundColor Green
    exit 0
}

Show-Message $summary "Snapdesk Setup"
Write-Host $summary -ForegroundColor Green
exit 0
