# Finds a real Python (not the Microsoft Store shim) and runs a script in this folder.
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File invoke_python.ps1 <script.py> [args...] [-Gui]
#   -Gui: run with pythonw.exe next to the chosen python.exe when present (GUI / no extra console).
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string] $ScriptName,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $ScriptArgs = @(),
    # Prefer pythonw.exe next to python.exe (no extra console when -Gui is used).
    [switch] $Gui
)

$ErrorActionPreference = "Continue"
$repo = $PSScriptRoot
$target = $null
$here = $repo
$foundIn = ""
foreach ($subdir in @("", "src")) {
    $dir = if ($subdir) { Join-Path $repo $subdir } else { $repo }
    $candidate = Join-Path $dir $ScriptName
    if (Test-Path -LiteralPath $candidate) {
        $target = $candidate
        $here = $dir
        $foundIn = $subdir
        break
    }
}
if (-not $target) {
    Write-Host "Not found: $ScriptName (searched repo root and src\)" -ForegroundColor Red
    exit 2
}
$srcDir = Join-Path $repo "src"
if ((Test-Path (Join-Path $srcDir "layout_manager.py")) -and $foundIn -eq "src") {
    if ($env:PYTHONPATH) {
        if ($env:PYTHONPATH -notlike "*$srcDir*") { $env:PYTHONPATH = "$srcDir;$env:PYTHONPATH" }
    } else {
        $env:PYTHONPATH = $srcDir
    }
}

function Test-RejectShim([string] $PythonExe) {
    if ([string]::IsNullOrWhiteSpace($PythonExe)) { return $true }
    if (-not (Test-Path -LiteralPath $PythonExe)) { return $true }
    if ($PythonExe -match "\\WindowsApps\\") { return $true }
    return $false
}

function Add-Unique {
    param([string] $Path, [System.Collections.Generic.List[string]] $List)
    if (-not (Test-RejectShim $Path)) {
        if (-not $List.Contains($Path)) { [void]$List.Add($Path) }
    }
}

function Test-PythonRuns([string] $Exe) {
    if (Test-RejectShim $Exe) { return $false }
    try {
        $out = & $Exe -c "import sys; print(sys.executable)" 2>&1
        $txt = ($out | Out-String)
        if ($LASTEXITCODE -ne 0) { return $false }
        if ($txt -match "Microsoft Store|install from the Microsoft Store") { return $false }
        return $true
    }
    catch {
        return $false
    }
}

function Rank-Exe([string] $p) {
    $n = [System.IO.Path]::GetFileName($p).ToLowerInvariant()
    switch ($n) {
        "pythonw.exe" { return 0 }
        "python.exe" { return 1 }
        "python3.exe" { return 2 }
        default { return 9 }
    }
}

function Resolve-PythonGuiLauncher([string] $Exe) {
    if ([string]::IsNullOrWhiteSpace($Exe)) { return $Exe }
    $n = [System.IO.Path]::GetFileName($Exe).ToLowerInvariant()
    if ($n -eq "pythonw.exe") { return $Exe }
    $dir = [System.IO.Path]::GetDirectoryName($Exe)
    if ([string]::IsNullOrWhiteSpace($dir)) { return $Exe }
    $w = Join-Path $dir "pythonw.exe"
    if (-not (Test-RejectShim $w) -and (Test-Path -LiteralPath $w) -and (Test-PythonRuns $w)) {
        return $w
    }
    return $Exe
}

$candidates = New-Object "System.Collections.Generic.List[string]"

# 1) py launcher — only if where.exe finds py.exe
$pyCmd = $null
try {
    $pyCmd = (& where.exe py 2>$null | Select-Object -First 1)
}
catch { }
if ($pyCmd -and -not (Test-RejectShim $pyCmd)) {
    try {
        $real = & py -3 -c "import sys; print(sys.executable)" 2>$null
        if ($LASTEXITCODE -eq 0 -and $real) {
            $one = ($real | Select-Object -Last 1).ToString().Trim()
            Add-Unique $one $candidates
        }
    }
    catch { }
}

# 2) where.exe for python / pythonw / python3 on PATH (skip shims)
foreach ($name in @("pythonw.exe", "python.exe", "python3.exe")) {
    try {
        & where.exe $name 2>$null | ForEach-Object {
            Add-Unique $_.Trim() $candidates
        }
    }
    catch { }
}

# 3) Every directory on PATH (covers installs not registered with where)
if ($env:PATH) {
    foreach ($dir in ($env:PATH -split ";" | Where-Object { $_ })) {
        foreach ($name in @("pythonw.exe", "python.exe", "python3.exe")) {
            Add-Unique (Join-Path $dir $name) $candidates
        }
    }
}

# 4) Registry (python.org installer)
foreach ($core in @(
        "HKLM:\SOFTWARE\Python\PythonCore",
        "HKCU:\SOFTWARE\Python\PythonCore",
        "HKLM:\SOFTWARE\WOW6432Node\Python\PythonCore"
    )) {
    if (-not (Test-Path -LiteralPath $core)) { continue }
    foreach ($ver in Get-ChildItem -LiteralPath $core -ErrorAction SilentlyContinue) {
        $ip = "$($ver.PSPath)\InstallPath"
        if (-not (Test-Path -LiteralPath $ip)) { continue }
        $prop = Get-ItemProperty -LiteralPath $ip -ErrorAction SilentlyContinue
        if (-not $prop) { continue }
        $dir = $prop.'(default)'
        if ([string]::IsNullOrWhiteSpace($dir)) { continue }
        $dir = $dir.TrimEnd("\")
        Add-Unique (Join-Path $dir "pythonw.exe") $candidates
        Add-Unique (Join-Path $dir "python.exe") $candidates
    }
}

# 5) Common install folders
$local = $env:LocalAppData
if ($local) {
    foreach ($n in @("Python314", "Python313", "Python312", "Python311", "Python310", "Python39")) {
        $base = Join-Path $local "Programs\Python\$n"
        Add-Unique (Join-Path $base "pythonw.exe") $candidates
        Add-Unique (Join-Path $base "python.exe") $candidates
    }
}

foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
    if (-not $pf) { continue }
    foreach ($n in @("Python314", "Python313", "Python312", "Python311", "Python310")) {
        $base = Join-Path $pf $n
        Add-Unique (Join-Path $base "pythonw.exe") $candidates
        Add-Unique (Join-Path $base "python.exe") $candidates
    }
}

$ordered = $candidates | Sort-Object { (Rank-Exe $_) }, { $_.Length }

foreach ($exe in $ordered) {
    if (-not (Test-PythonRuns $exe)) { continue }
    $launchExe = if ($Gui) { Resolve-PythonGuiLauncher $exe } else { $exe }
    # Diagnostics must not go to stdout — GUI parses JSON from list-windows.
    [void][Console]::Error.WriteLine("Using: $launchExe")
    # Do not use Start-Process -NoNewWindow with pythonw — on many PCs the wait/detach
    # behavior is wrong and the host exits immediately (looks like "flash and close").
    Push-Location $here
    try {
        $argList = @($target) + @($ScriptArgs)
        & $launchExe @argList
        $exitCode = 0
        if ($null -ne $LASTEXITCODE) { $exitCode = [int]$LASTEXITCODE }
        exit $exitCode
    }
    finally {
        Pop-Location
    }
}

Write-Host ""
Write-Host "No working Python was found on this PC." -ForegroundColor Yellow
Write-Host ""
Write-Host "Install Python (one step), then run run_winui.cmd or layout.cmd:" -ForegroundColor White
Write-Host "  powershell -NoProfile -ExecutionPolicy Bypass -File ""$repo\scripts\install_python.ps1""" -ForegroundColor Cyan
Write-Host ""
Write-Host "Or manually:" -ForegroundColor White
Write-Host "  winget install -e --id Python.Python.3.12 --accept-package-agreements --accept-source-agreements" -ForegroundColor White
Write-Host "  https://www.python.org/downloads/  (tick Add python.exe to PATH)" -ForegroundColor White
Write-Host "  Turn OFF App Execution Aliases for python.exe (Settings > Apps)" -ForegroundColor White
Write-Host ""
exit 1
