# build-local.ps1
param (
    [string]$Configuration = "Debug",
    # Folder containing the .NET 8 Spry.dll (defaults to the per-user Spry install)
    [string]$SpryDir = "$env:LOCALAPPDATA\Spry\current"
)

$ErrorActionPreference = "Stop"

Write-Host "1. Building merz.dll ($Configuration)..." -ForegroundColor Cyan
if (-not (Test-Path (Join-Path $SpryDir "Spry.dll"))) {
    Write-Error "Spry.dll not found in '$SpryDir'. Pass -SpryDir <folder containing the .NET 8 Spry.dll>."
    exit 1
}
dotnet build merz_src/merz.csproj --configuration $Configuration -p:SpryDir="$SpryDir"

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed."
    exit 1
}

# Define paths
$buildOutput = Join-Path "merz_src/bin" "$Configuration/net8.0-windows"
$configFileSource = "pm-spry-tools/merz-config.json"
$configFileDest = Join-Path $buildOutput "merz-config.json"

# Check if custom config exists
if (Test-Path $configFileSource) {
    Write-Host "2. Copying custom configuration..." -ForegroundColor Cyan
    Copy-Item $configFileSource $configFileDest -Force
    Write-Host "   Copied '$configFileSource' -> '$configFileDest'" -ForegroundColor Green
} else {
    Write-Warning "Custom config file '$configFileSource' not found. merz will use default settings (All Tools)."
}

Write-Host "`nBuild complete!" -ForegroundColor Green
Write-Host "merz.dll is located at:" 
Write-Host "  $PWD/$buildOutput/merz.dll" -ForegroundColor Yellow
