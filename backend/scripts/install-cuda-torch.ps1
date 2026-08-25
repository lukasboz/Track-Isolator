# Detect NVIDIA GPU and install CUDA-enabled PyTorch
# Run from the backend directory

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BackendDir = Split-Path -Parent $ScriptDir
$VenvPip = Join-Path (Join-Path (Join-Path $BackendDir "venv") "Scripts") "pip.exe"
$VenvPython = Join-Path (Join-Path (Join-Path $BackendDir "venv") "Scripts") "python.exe"

Write-Host "=== CUDA PyTorch Installer ===" -ForegroundColor Cyan

# Check for NVIDIA GPU
Write-Host "`nDetecting GPU..."
$gpu = Get-WmiObject Win32_VideoController | Where-Object { $_.Name -like "*NVIDIA*" }

if (-not $gpu) {
    Write-Host "No NVIDIA GPU detected. CUDA acceleration not available." -ForegroundColor Yellow
    Write-Host "Continuing with CPU-only PyTorch."
    exit 0
}

Write-Host "Found: $($gpu.Name)" -ForegroundColor Green

# Check current torch
Write-Host "`nChecking current PyTorch installation..."
$cudaCheck = & $VenvPython -c "import torch; print('CUDA' if torch.cuda.is_available() else 'CPU')" 2>&1
Write-Host "Current mode: $cudaCheck"

if ($cudaCheck -eq "CUDA") {
    Write-Host "CUDA PyTorch is already installed!" -ForegroundColor Green
    & $VenvPython -c "import torch; print(f'GPU: {torch.cuda.get_device_name(0)}'); print(f'CUDA: {torch.version.cuda}')"
    exit 0
}

# Install CUDA PyTorch
Write-Host "`nInstalling CUDA-enabled PyTorch (this will download ~2GB)..." -ForegroundColor Yellow
& $VenvPip install torch==2.1.2+cu118 torchaudio==2.1.2+cu118 --index-url https://download.pytorch.org/whl/cu118 --force-reinstall

# Verify
Write-Host "`nVerifying CUDA installation..."
$result = & $VenvPython -c "import torch; print('CUDA available:', torch.cuda.is_available()); print('Device:', torch.cuda.get_device_name(0) if torch.cuda.is_available() else 'N/A')" 2>&1
Write-Host $result

if ($result -match "CUDA available: True") {
    Write-Host "`nCUDA PyTorch installed successfully!" -ForegroundColor Green
    Write-Host "Stem separation will now use GPU acceleration (~10x faster)."
} else {
    Write-Host "`nCUDA installation may have failed. Check the output above." -ForegroundColor Red
}
