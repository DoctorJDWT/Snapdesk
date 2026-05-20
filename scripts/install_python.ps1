# Installs Python 3.12 with winget, then pip-installs requirements for Snapdesk.
# Run:  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install_python.ps1

$ErrorActionPreference = "Continue"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$req = Join-Path $root "requirements.txt"

function Find-PythonExe {
    $roots = @(
        "$env:LocalAppData\Programs\Python",
        $env:ProgramFiles,
        ${env:ProgramFiles(x86)}
    )
    foreach ($r in $roots) {
        if (-not $r -or -not (Test-Path -LiteralPath $r)) { continue }
        try {
            Get-ChildItem -Path $r -Directory -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match '^Python' } |
                ForEach-Object {
                    foreach ($name in @("python.exe", "pythonw.exe")) {
                        $p = Join-Path $_.FullName $name
                        if ($p -match "\\WindowsApps\\") { continue }
                        if (Test-Path -LiteralPath $p) { return $p }
                    }
                }
        }
        catch { }
    }
    try {
        $w = & where.exe python 2>$null | Where-Object { $_ -notmatch "\\WindowsApps\\" } | Select-Object -First 1
        if ($w -and (Test-Path -LiteralPath $w.Trim())) { return $w.Trim() }
    }
    catch { }
    return $null
}

Write-Host "=== Snapdesk: install Python ===" -ForegroundColor Cyan
Write-Host ""

$winget = $null
try {
    $winget = (& where.exe winget 2>$null | Select-Object -First 1)
}
catch { }

if ($winget) {
    foreach ($pkg in @("Python.Python.3.12", "Python.Python.3.13", "Python.Python.3.11")) {
        Write-Host "Trying winget install $pkg ..." -ForegroundColor Yellow
        & winget install -e --id $pkg --accept-package-agreements --accept-source-agreements --disable-interactivity
        Write-Host "(winget exit code: $LASTEXITCODE)" -ForegroundColor DarkGray
        Start-Sleep -Seconds 2
        $py = Find-PythonExe
        if ($py) { break }
    }
}
else {
    Write-Host "winget not found on PATH. Install Python from https://www.python.org/downloads/" -ForegroundColor Yellow
}

$py = Find-PythonExe
if (-not $py) {
    Write-Host ""
    Write-Host "Python executable still not found. After installing, close and reopen PowerShell, then run this script again." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Found: $py" -ForegroundColor Green
& $py --version

Write-Host ""
Write-Host "Installing pip dependencies..." -ForegroundColor Yellow
& $py -m pip install --upgrade pip
& $py -m pip install -r $req
if ($LASTEXITCODE -ne 0) {
    Write-Host "pip install had errors. Try: & '$py' -m pip install -r '$req'" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Done. Start Snapdesk with:  run_winui.cmd" -ForegroundColor Green
exit 0
