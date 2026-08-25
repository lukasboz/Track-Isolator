# Build script for Stem Separator Backend on Windows
# Creates a standalone PyInstaller bundle

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BackendDir = Split-Path -Parent $ScriptDir
$PyInstallerDir = Join-Path $BackendDir "pyinstaller"
$FfmpegDir = Join-Path $BackendDir "ffmpeg\windows"
$FfmpegTarget = Join-Path $FfmpegDir "ffmpeg.exe"
$FfprobeTarget = Join-Path $FfmpegDir "ffprobe.exe"

function Resolve-MediaTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $wingetPackages = Join-Path $env:LOCALAPPDATA "Microsoft\WinGet\Packages"
    if (Test-Path $wingetPackages) {
        $wingetMatch = Get-ChildItem $wingetPackages -Filter "$Name.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "Gyan\.FFmpeg" } |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -ne $wingetMatch) {
            return $wingetMatch.FullName
        }
    }

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    return $null
}

Write-Host "=== Stem Separator Backend Builder (Windows) ===" -ForegroundColor Cyan

# Resolve an exact Python 3.10 interpreter for creating the build environment.
Write-Host "`nChecking Python 3.10 installation..."
$pyLauncher = Get-Command py -ErrorAction SilentlyContinue
$systemPython = $null

if ($null -ne $pyLauncher) {
    & $pyLauncher.Source -3.10 -c "import sys; assert sys.version_info[:2] == (3, 10); print(sys.version)"
    if ($LASTEXITCODE -ne 0) {
        throw "The Python launcher exists, but Python 3.10 is not installed."
    }
    $createVenv = {
        param([string]$Destination)
        & $pyLauncher.Source -3.10 -m venv $Destination
    }
}
else {
    $systemPython = Get-Command python -ErrorAction SilentlyContinue
    if ($null -eq $systemPython) {
        throw "Python 3.10 was not found. Install package Python.Python.3.10."
    }
    & $systemPython.Source -c "import sys; assert sys.version_info[:2] == (3, 10); print(sys.version)"
    if ($LASTEXITCODE -ne 0) {
        throw "The python command is not Python 3.10."
    }
    $createVenv = {
        param([string]$Destination)
        & $systemPython.Source -m venv $Destination
    }
}

# Create virtual environment if it doesn't exist.
$venvDir = Join-Path $BackendDir "venv"
if (-not (Test-Path $venvDir)) {
    Write-Host "`nCreating virtual environment..."
    & $createVenv $venvDir
    if ($LASTEXITCODE -ne 0) { throw "Failed to create the backend virtual environment." }
}

$venvPython = Join-Path (Join-Path $venvDir "Scripts") "python.exe"
if (-not (Test-Path $venvPython)) {
    throw "Virtual-environment Python was not created at $venvPython."
}
Write-Host "Using: $venvPython"

# Install the pinned local-backend dependencies.
Write-Host "`nInstalling dependencies..."
& $venvPython -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "Failed to update pip." }
& $venvPython -m pip install torch==2.1.2 torchaudio==2.1.2 --index-url https://download.pytorch.org/whl/cpu
if ($LASTEXITCODE -ne 0) { throw "Failed to install CPU PyTorch." }
& $venvPython -m pip install -r (Join-Path $BackendDir "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "Failed to install backend requirements." }

# Copy the installed FFmpeg tools into the bundle input directory. The
# release remains local-only and does not download media tools at runtime.
$ffmpegSource = Resolve-MediaTool "ffmpeg"
$ffprobeSource = Resolve-MediaTool "ffprobe"
if ($null -eq $ffmpegSource -or $null -eq $ffprobeSource) {
    throw "FFmpeg and FFprobe were not found. Install package Gyan.FFmpeg and open a new PowerShell session."
}
New-Item -ItemType Directory -Path $FfmpegDir -Force | Out-Null
Copy-Item $ffmpegSource $FfmpegTarget -Force
Copy-Item $ffprobeSource $FfprobeTarget -Force
Write-Host "Using FFmpeg: $FfmpegTarget"
Write-Host "Using FFprobe: $FfprobeTarget"

# Download and verify htdemucs once, then include it in the release bundle.
Write-Host "`nCaching Demucs model weights..."
$CacheScript = Join-Path (Join-Path $BackendDir "scripts") "cache-demucs-model.py"
& $venvPython $CacheScript
if ($LASTEXITCODE -ne 0) { throw "Failed to cache the Demucs model." }

# Run PyInstaller
Write-Host "`nRunning PyInstaller..."
Push-Location $PyInstallerDir
& $venvPython -m PyInstaller backend.spec --noconfirm --clean
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed." }
Pop-Location

# Verify output
$distDir = Join-Path (Join-Path $PyInstallerDir "dist") "stem-separator-backend"
if (Test-Path $distDir) {
    $size = (Get-ChildItem $distDir -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host "`nBuild successful!" -ForegroundColor Green
    Write-Host "Output: $distDir"
    Write-Host "Size: $([math]::Round($size, 1)) MB"
} else {
    Write-Host "`nBuild FAILED - output directory not found" -ForegroundColor Red
    exit 1
}

Write-Host "`nDone." -ForegroundColor Cyan
