# Maintainer: publish Snapdesk and pack a setup zip for end users.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishScript = Join-Path $PSScriptRoot "publish_snapdesk.ps1"
$dist = Join-Path $root "dist\Snapdesk"
$zipPath = Join-Path $root "dist\Snapdesk-Setup.zip"
$stage = Join-Path $root "dist\Snapdesk-Setup"

Set-Location $root

Write-Host "=== publish_snapdesk.ps1 ===" -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File $publishScript
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if (-not (Test-Path -LiteralPath $dist)) {
    Write-Error "Publish output missing: $dist"
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $root "Install Snapdesk.cmd") -Destination $stage -Force
New-Item -ItemType Directory -Path (Join-Path $stage "scripts") -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root "scripts\Install-Snapdesk.ps1") -Destination (Join-Path $stage "scripts\Install-Snapdesk.ps1") -Force
Copy-Item -LiteralPath $dist -Destination (Join-Path $stage "dist\Snapdesk") -Recurse -Force

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
New-Item -ItemType Directory -Path (Split-Path $zipPath) -Force | Out-Null
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Setup package staged at: $stage" -ForegroundColor Green
Write-Host "Zip for distribution:       $zipPath" -ForegroundColor Green
Write-Host ""
Write-Host "End users: extract the zip, then double-click Install Snapdesk.cmd"
exit 0
