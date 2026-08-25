#!/bin/bash
# Build script for Stem Separator Backend on macOS
# Creates a standalone PyInstaller bundle

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BACKEND_DIR="$(dirname "$SCRIPT_DIR")"
PYINSTALLER_DIR="$BACKEND_DIR/pyinstaller"

echo "=== Stem Separator Backend Builder (macOS) ==="

# Check Python version
echo ""
echo "Checking Python installation..."
python3 --version

# Create virtual environment if it doesn't exist
VENV_DIR="$BACKEND_DIR/venv"
if [ ! -d "$VENV_DIR" ]; then
    echo ""
    echo "Creating virtual environment..."
    python3 -m venv "$VENV_DIR"
fi

# Activate venv
source "$VENV_DIR/bin/activate"

# Install dependencies. The standard macOS PyTorch wheel includes CPU support
# and exposes MPS on compatible Apple Silicon systems, so a second reinstall
# from the CPU-only index is unnecessary.
echo ""
echo "Installing dependencies..."
pip install --upgrade pip
pip install -r "$BACKEND_DIR/requirements.txt"

# Download and verify htdemucs once, then include it in the one-directory
# bundle. This removes the first-launch network download.
echo ""
echo "Caching Demucs model weights..."
python "$BACKEND_DIR/scripts/cache-demucs-model.py"

# Run PyInstaller
echo ""
echo "Running PyInstaller..."
cd "$PYINSTALLER_DIR"
pyinstaller backend.spec --noconfirm --clean

# Verify output
DIST_DIR="$PYINSTALLER_DIR/dist/stem-separator-backend"
if [ -d "$DIST_DIR" ]; then
    SIZE=$(du -sh "$DIST_DIR" | cut -f1)
    echo ""
    echo "Build successful!"
    echo "Output: $DIST_DIR"
    echo "Size: $SIZE"
else
    echo ""
    echo "Build FAILED - output directory not found"
    exit 1
fi

echo ""
echo "Done."
