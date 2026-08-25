"""
Runtime hook for PyTorch / Demucs.
Sets environment variables before any imports occur so that
torch, torchaudio, and demucs can locate their resources correctly
when running from a PyInstaller bundle.
"""

import os
import sys


def _get_base_dir():
    """Get the base directory of the PyInstaller bundle."""
    if getattr(sys, "frozen", False):
        return sys._MEIPASS
    return os.path.dirname(os.path.abspath(__file__))


base_dir = _get_base_dir()

# Set TORCH_HOME so model downloads go to a persistent user directory
if not os.environ.get("TORCH_HOME"):
    os.environ["TORCH_HOME"] = os.path.join(os.path.expanduser("~"), ".cache", "torch")

# Ensure the torch cache directory exists
os.makedirs(os.environ["TORCH_HOME"], exist_ok=True)

# Add bundled FFmpeg to PATH if present
ffmpeg_dir = base_dir
if os.path.exists(os.path.join(ffmpeg_dir, "ffmpeg")) or os.path.exists(
    os.path.join(ffmpeg_dir, "ffmpeg.exe")
):
    os.environ["PATH"] = ffmpeg_dir + os.pathsep + os.environ.get("PATH", "")

# Also check FFMPEG_PATH env var (set by the native app)
ffmpeg_path = os.environ.get("FFMPEG_PATH")
if ffmpeg_path and os.path.isdir(ffmpeg_path):
    os.environ["PATH"] = ffmpeg_path + os.pathsep + os.environ.get("PATH", "")
