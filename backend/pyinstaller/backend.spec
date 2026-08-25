# -*- mode: python ; coding: utf-8 -*-
"""
PyInstaller spec file for Stem Separator Backend.
Builds a single-directory bundle containing the FastAPI server,
Demucs model code, PyTorch, and FFmpeg.

Usage:
    pyinstaller backend.spec

Output:
    dist/stem-separator-backend/
"""

import sys
import os
from pathlib import Path

block_cipher = None

# Determine platform-specific media binaries.
platform_media_binaries = []
if sys.platform == "win32":
    ffmpeg_path = os.path.join("..", "ffmpeg", "windows", "ffmpeg.exe")
    ffprobe_path = os.path.join("..", "ffmpeg", "windows", "ffprobe.exe")
    platform_media_binaries = [
        (path, ".")
        for path in (ffmpeg_path, ffprobe_path)
        if os.path.exists(path)
    ]
elif sys.platform == "darwin":
    ffmpeg_path = os.path.join("..", "ffmpeg", "macos", "ffmpeg")
    if os.path.exists(ffmpeg_path):
        platform_media_binaries = [(ffmpeg_path, ".")]

# Release builds prefetch htdemucs into this local repository so the packaged
# desktop app can start without downloading model weights on first launch.
model_datas = []
models_path = os.path.join("..", "models")
if os.path.isdir(models_path):
    model_datas = [(models_path, "models")]

a = Analysis(
    [os.path.join("..", "app.py")],
    pathex=[],
    binaries=platform_media_binaries,
    datas=model_datas,
    hiddenimports=[
        # Demucs
        "demucs",
        "demucs.pretrained",
        "demucs.apply",
        "demucs.audio",
        "demucs.hdemucs",
        "demucs.htdemucs",
        "demucs.states",
        "demucs.repo",
        "demucs.spec",
        "demucs.utils",
        # PyTorch
        "torch",
        "torch.nn",
        "torch.nn.functional",
        "torchaudio",
        "torchaudio.backend",
        "torchaudio.backend.sox_io_backend",
        "torchaudio.backend.soundfile_backend",
        # FastAPI / ASGI stack
        "fastapi",
        "fastapi.middleware",
        "fastapi.middleware.cors",
        "starlette",
        "starlette.responses",
        "starlette.routing",
        "starlette.middleware",
        "starlette.middleware.cors",
        "pydantic",
        "pydantic.deprecated",
        "pydantic.deprecated.decorator",
        # Uvicorn
        "uvicorn",
        "uvicorn.logging",
        "uvicorn.loops",
        "uvicorn.loops.auto",
        "uvicorn.protocols",
        "uvicorn.protocols.http",
        "uvicorn.protocols.http.auto",
        "uvicorn.protocols.http.h11_impl",
        "uvicorn.protocols.http.httptools_impl",
        "uvicorn.protocols.websockets",
        "uvicorn.protocols.websockets.auto",
        "uvicorn.lifespan",
        "uvicorn.lifespan.on",
        "uvicorn.lifespan.off",
        # Supporting
        "multipart",
        "python_multipart",
        "anyio",
        "anyio._backends",
        "anyio._backends._asyncio",
        "sniffio",
        "httptools",
        "h11",
        "watchfiles",
        # Encoding
        "encodings",
        "encodings.utf_8",
        "encodings.ascii",
        "encodings.latin_1",
    ],
    hookspath=[os.path.join("hooks")],
    runtime_hooks=[os.path.join("runtime-hooks", "pyi_rth_torch.py")],
    excludes=[
        "tkinter",
        "matplotlib",
        "notebook",
        "jupyterlab",
        "IPython",
        "scipy",
        "pandas",
        "PIL",
        "cv2",
    ],
    noarchive=False,
)

pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name="stem-separator-backend",
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=False,  # Don't UPX torch libraries
    console=True,
)

coll = COLLECT(
    exe,
    a.binaries,
    a.zipfiles,
    a.datas,
    strip=False,
    upx=False,
    name="stem-separator-backend",
)
