param(
    [switch]$SkipBackendBuild
)

# Full local-only release build for Windows.
# Produces a self-contained UI executable plus its bundled local backend.

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$WindowsDir = Split-Path -Parent $ScriptDir
$RootDir = Split-Path -Parent $WindowsDir
$ProjectFile = Join-Path $WindowsDir "StemSeparator\StemSeparator.csproj"
$ReleaseDir = Join-Path $WindowsDir "build\release"
$ArchivePath = Join-Path $WindowsDir "build\StemSeparator-Windows-x64.zip"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Stem Separator - Windows Release Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$BackendScript = Join-Path (Join-Path (Join-Path $RootDir "backend") "scripts") "build-backend-windows.ps1"
if (-not $SkipBackendBuild) {
    Write-Host "`n[1/4] Building Python backend..." -ForegroundColor Yellow
    & $BackendScript
}
else {
    Write-Host "`n[1/4] Reusing existing Python backend build..." -ForegroundColor Yellow
}

# Step 2: Build the self-contained .NET app.
Write-Host "`n[2/4] Building .NET WPF application..." -ForegroundColor Yellow
if (Test-Path $ReleaseDir) {
    Remove-Item -Recurse -Force $ReleaseDir
}
dotnet publish $ProjectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $ReleaseDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

# Step 3: Bundle backend into release output
Write-Host "`n[3/4] Bundling backend into release..." -ForegroundColor Yellow
$BackendDist = Join-Path (Join-Path (Join-Path (Join-Path $RootDir "backend") "pyinstaller") "dist") "stem-separator-backend"
$DestBackend = Join-Path $ReleaseDir "backend"
if (-not (Test-Path $BackendDist)) {
    throw "Backend build output was not found at $BackendDist."
}
if (Test-Path $DestBackend) {
    Remove-Item -Recurse -Force $DestBackend
}
Copy-Item -Recurse $BackendDist $DestBackend

# Step 4: Create a portable ZIP and print the result.
Write-Host "`n[4/4] Creating portable archive..." -ForegroundColor Yellow
if (Test-Path $ArchivePath) {
    Remove-Item -Force $ArchivePath
}
Compress-Archive -Path (Join-Path $ReleaseDir "*") -DestinationPath $ArchivePath

$ExePath = Join-Path $ReleaseDir "StemSeparator.exe"
if (-not (Test-Path $ExePath)) {
    throw "The published application executable was not created."
}

Write-Host "`nBuild complete!" -ForegroundColor Green
$TotalSize = (Get-ChildItem $ReleaseDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host "Output: $ReleaseDir"
Write-Host "Executable: $ExePath"
Write-Host "Portable ZIP: $ArchivePath"
Write-Host "Total size: $([math]::Round($TotalSize, 1)) MB"
