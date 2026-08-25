# Bundle the PyInstaller-built backend into the Windows app output directory
# Run this after building the backend and before creating the installer

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WindowsDir = Split-Path -Parent $ScriptDir
$RootDir = Split-Path -Parent $WindowsDir
$BackendDist = Join-Path (Join-Path (Join-Path (Join-Path $RootDir "backend") "pyinstaller") "dist") "stem-separator-backend"

# The release script publishes the UI here.
$PublishDir = Join-Path $WindowsDir "build\release"

Write-Host "=== Bundling Backend into Windows App ===" -ForegroundColor Cyan
Write-Host "Backend source: $BackendDist"
Write-Host "App output: $PublishDir"

# Check backend exists
if (-not (Test-Path $BackendDist)) {
    Write-Host "ERROR: Backend not built. Run backend/scripts/build-backend-windows.ps1 first." -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $PublishDir)) {
    Write-Host "ERROR: App output not found. Run windows/scripts/build-release.ps1 first." -ForegroundColor Red
    exit 1
}

# Copy backend into app output
$DestBackend = Join-Path $PublishDir "backend"
if (Test-Path $DestBackend) {
    Remove-Item -Recurse -Force $DestBackend
}
Write-Host "Copying backend files..."
Copy-Item -Recurse $BackendDist $DestBackend

$Size = (Get-ChildItem $DestBackend -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Backend bundled successfully ($([math]::Round($Size, 1)) MB)" -ForegroundColor Green
