$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-snapdesk.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'LayoutProfiles.WinUI\bin\x64\Release\net8.0-windows10.0.19041.0'
$exe = Join-Path $out 'Snapdesk.exe'
if (-not (Test-Path $exe)) {
    Write-Error "Snapdesk.exe not found at $exe"
    exit 1
}

Start-Process -FilePath $exe -WorkingDirectory $out
Write-Host "Launched Snapdesk from $out"
