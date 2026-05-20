$ErrorActionPreference = 'Stop'
Stop-Process -Name Snapdesk, LayoutProfiles.WinUI -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'LayoutProfiles.WinUI\LayoutProfiles.WinUI.csproj'
dotnet build $proj -c Release -p:Platform=x64
exit $LASTEXITCODE
