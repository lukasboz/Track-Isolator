#!/bin/bash
# Detect NVIDIA GPU and install CUDA-enabled PyTorch (Linux/macOS)

set -e
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BACKEND_DIR="$(dirname "$SCRIPT_DIR")"

echo "=== CUDA PyTorch Installer ==="

# Check for NVIDIA GPU
echo ""
echo "Detecting GPU..."
if command -v nvidia-smi &> /dev/null; then
    GPU_NAME=$(nvidia-smi --query-gpu=name --format=csv,noheader 2>/dev/null | head -1)
    echo "Found: $GPU_NAME"
else
    echo "No NVIDIA GPU detected (nvidia-smi not found)."
    echo "Continuing with CPU-only PyTorch."
    exit 0
fi

# Check current torch
echo ""
echo "Checking current PyTorch installation..."
CUDA_CHECK=$("$BACKEND_DIR/venv/bin/python" -c "import torch; print('CUDA' if torch.cuda.is_available() else 'CPU')" 2>&1)
echo "Current mode: $CUDA_CHECK"

if [ "$CUDA_CHECK" = "CUDA" ]; then
    echo "CUDA PyTorch is already installed!"
    exit 0
fi

# Install CUDA PyTorch
echo ""
echo "Installing CUDA-enabled PyTorch (~2GB download)..."
"$BACKEND_DIR/venv/bin/pip" install torch==2.1.2+cu118 torchaudio==2.1.2+cu118 \
    --index-url https://download.pytorch.org/whl/cu118 --force-reinstall

# Verify
echo ""
echo "Verifying..."
"$BACKEND_DIR/venv/bin/python" -c "
import torch
print('CUDA available:', torch.cuda.is_available())
if torch.cuda.is_available():
    print('Device:', torch.cuda.get_device_name(0))
    print('CUDA version:', torch.version.cuda)
"

echo ""
echo "Done. Restart the backend to use GPU acceleration."
