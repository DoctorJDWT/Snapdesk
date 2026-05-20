# Build LayoutProfiles.WinUI with a detailed log (build.log in repo root).
# If execution policy blocks this script, use launchers\build_winui.cmd instead.

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$proj = Join-Path $root "LayoutProfiles.WinUI\LayoutProfiles.WinUI.csproj"
$log = Join-Path $root "build.log"

Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found. Install .NET 8 SDK and open a new PowerShell window."
}

Write-Host "=== dotnet restore ==="
dotnet restore $proj 2>&1 | Tee-Object -FilePath $log

Write-Host "=== dotnet build (Release, x64) ==="
dotnet build $proj -c Release -p:Platform=x64 -v:n --no-restore 2>&1 | Tee-Object -FilePath $log -Append
$exit = $LASTEXITCODE

if ($exit -ne 0) {
    Write-Host ""
    Write-Host "Build failed. See $log"
    Select-String -Path $log -Pattern "error|WMC|Xaml|XAML" -CaseSensitive:$false | Select-Object -First 40
    exit $exit
}

Write-Host "Build succeeded. Run: launchers\run_winui.cmd  or  Open Snapdesk.cmd"
exit 0
