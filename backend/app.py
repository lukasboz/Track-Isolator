import argparse
import asyncio
import json
import math
import os
import shutil
import signal
import subprocess
import sys
import tempfile
import threading
import time
import uuid
from pathlib import Path
from typing import AsyncGenerator

from fastapi import FastAPI, File, UploadFile, HTTPException, Query
from fastapi.responses import StreamingResponse, FileResponse
from fastapi.middleware.cors import CORSMiddleware

# Let unsupported MPS operations fall back to CPU instead of failing an entire
# separation. Demucs 4 already routes its complex-number operations through
# CPU, while the supported layers can still run on Apple Silicon's GPU.
os.environ.setdefault("PYTORCH_ENABLE_MPS_FALLBACK", "1")

import torch
from demucs.pretrained import get_model
from demucs.apply import apply_model
from demucs.audio import AudioFile, save_audio

app = FastAPI(title="Stem Separator Backend")

DEFAULT_ALLOWED_ORIGINS = [
    "http://127.0.0.1:3000",
    "http://localhost:3000",
]
configured_origins = os.environ.get("STEM_SEPARATOR_ALLOWED_ORIGINS", "")
allowed_origins = [
    origin.strip()
    for origin in configured_origins.split(",")
    if origin.strip()
] or DEFAULT_ALLOWED_ORIGINS

# Native clients do not use CORS. Browser access is restricted to local
# development origins unless an explicit hosted UI origin is configured.
app.add_middleware(
    CORSMiddleware,
    allow_origins=allowed_origins,
    allow_credentials=False,
    allow_methods=["GET", "POST", "DELETE", "OPTIONS"],
    allow_headers=["Content-Type"],
)


@app.middleware("http")
async def allow_local_network_requests(request, call_next):
    """
    Allow an HTTPS-hosted Vercel UI to reach the loopback backend.

    Chromium's Private Network Access preflight expects this response header
    before a public website can call a service on 127.0.0.1.
    """
    response = await call_next(request)
    if (
        request.headers.get("access-control-request-private-network") == "true"
        or request.headers.get("origin")
    ):
        response.headers["Access-Control-Allow-Private-Network"] = "true"
    return response


# Directories - use temp directory
temp_base = Path(tempfile.gettempdir()) / "stem-separator"
UPLOAD_DIR = temp_base / "uploads"
OUTPUT_DIR = temp_base / "outputs"
UPLOAD_DIR.mkdir(parents=True, exist_ok=True)
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

# Set up FFmpeg path if provided via environment variable
ffmpeg_path = os.environ.get("FFMPEG_PATH")
if ffmpeg_path:
    os.environ["PATH"] = ffmpeg_path + os.pathsep + os.environ.get("PATH", "")

# Model cache - supports multiple models
print("Initializing Stem Separator Backend...")
models: dict[str, torch.nn.Module] = {}
model_load_lock = threading.Lock()
inference_lock = asyncio.Lock()
conversion_locks: dict[Path, asyncio.Lock] = {}
MAX_UPLOAD_BYTES = int(
    os.environ.get("STEM_SEPARATOR_MAX_UPLOAD_BYTES", str(50 * 1024 * 1024))
)


class UploadTooLargeError(Exception):
    pass


def select_device() -> str:
    """Choose the fastest available full-quality execution device."""
    requested = os.environ.get("STEM_SEPARATOR_DEVICE", "auto").strip().lower()

    if requested == "auto":
        if torch.cuda.is_available():
            return "cuda"
        if hasattr(torch.backends, "mps") and torch.backends.mps.is_available():
            return "mps"
        return "cpu"

    if requested == "cpu":
        return requested
    if requested.startswith("cuda") and torch.cuda.is_available():
        return requested
    if (
        requested == "mps"
        and hasattr(torch.backends, "mps")
        and torch.backends.mps.is_available()
    ):
        return requested

    print(
        f"Requested device '{requested}' is unavailable; falling back to CPU.",
        file=sys.stderr,
    )
    return "cpu"


device = select_device()
print(f"Using device: {device}")


def find_bundled_model_repo(model_name: str) -> Path | None:
    """Locate model files included by the PyInstaller release build."""
    candidates = [
        Path(__file__).resolve().parent / "models",
        Path(getattr(sys, "_MEIPASS", Path(sys.executable).resolve().parent))
        / "models",
        Path(sys.executable).resolve().parent / "models",
    ]
    for candidate in candidates:
        if (candidate / f"{model_name}.yaml").is_file():
            return candidate
    return None


def load_model(model_name: str = "htdemucs"):
    model = models.get(model_name)
    if model is not None:
        return model

    # Startup normally loads htdemucs before requests arrive, but the lock also
    # prevents duplicate downloads/allocations if another model is requested
    # concurrently for the first time.
    with model_load_lock:
        model = models.get(model_name)
        if model is None:
            print(f"Loading model: {model_name}...")
            local_repo = find_bundled_model_repo(model_name)
            if local_repo is not None:
                print(f"Using bundled model repository: {local_repo}")
            model = get_model(model_name, repo=local_repo)
            model.to(device)
            model.eval()
            models[model_name] = model
            print(f"Model {model_name} loaded successfully!")
    return model


# Stem names per model
STEM_NAMES = {
    "htdemucs": ["drums", "bass", "other", "vocals"],
    "htdemucs_6s": ["drums", "bass", "other", "vocals", "guitar", "piano"],
}
SUPPORTED_AUDIO_EXTENSIONS = {
    ".mp3",
    ".wav",
    ".flac",
    ".aac",
    ".ogg",
    ".m4a",
    ".wma",
    ".aif",
    ".aiff",
}


@app.on_event("startup")
async def startup_event():
    load_model("htdemucs")


@app.get("/health")
async def health_check():
    gpu_name = None
    if torch.cuda.is_available():
        try:
            gpu_name = torch.cuda.get_device_name(0)
        except Exception:
            pass
    return {
        "status": "healthy",
        "service": "stem-separator-local",
        "device": device,
        "gpu_name": gpu_name,
        "model_loaded": len(models) > 0,
        "available_models": list(STEM_NAMES.keys()),
    }


def sse_event(payload: dict) -> str:
    return f"data: {json.dumps(payload)}\n\n"


async def process_audio_streaming(
    file_path: Path,
    output_dir: Path,
    model_name: str = "htdemucs",
    preview: bool = False,
) -> AsyncGenerator[str, None]:
    succeeded = False
    try:
        start_time = time.perf_counter()

        yield sse_event({"status": "Loading model...", "progress": 5})

        demucs_model = load_model(model_name)

        yield sse_event({"status": "Loading audio file...", "progress": 10})

        def load_audio_sync():
            return AudioFile(str(file_path)).read(
                streams=0,
                samplerate=demucs_model.samplerate,
                channels=demucs_model.audio_channels,
            )

        wav = await asyncio.to_thread(load_audio_sync)
        shutil.rmtree(file_path.parent, ignore_errors=True)

        # Preview mode: truncate to first 30 seconds
        if preview:
            max_samples = int(30 * demucs_model.samplerate)
            if wav.shape[-1] > max_samples:
                wav = wav[..., :max_samples]

        ref = wav.mean(0)
        ref_mean = ref.mean()
        ref_std = ref.std()
        ref_std_value = float(ref_std)
        if not math.isfinite(ref_std_value) or ref_std_value <= torch.finfo(wav.dtype).eps:
            raise ValueError("Audio is silent or has an invalid signal level.")

        # In-place normalization avoids two track-sized temporary tensors while
        # using the same arithmetic as Demucs's reference separation command.
        wav.sub_(ref_mean).div_(ref_std)
        del ref

        yield sse_event(
            {
                "status": "Separating stems... (this may take 1-3 minutes)",
                "progress": 20,
            }
        )

        def apply_model_sync():
            with torch.inference_mode():
                sources = apply_model(
                    demucs_model,
                    wav[None].to(device),
                    device=device,
                    shifts=1,
                    split=True,
                    overlap=0.25,
                    progress=False,
                )[0]
                sources.mul_(ref_std).add_(ref_mean)
                return sources.cpu()

        # Demucs can exhaust GPU memory or oversubscribe CPU cores when two
        # requests enter the same process at once. Queueing inference keeps
        # throughput predictable while uploads, downloads, and encoding remain
        # concurrent.
        async with inference_lock:
            sources = await asyncio.to_thread(apply_model_sync)

        del wav
        yield sse_event({"status": "Saving stems...", "progress": 85})

        stem_names = list(
            getattr(demucs_model, "sources", STEM_NAMES.get(model_name, []))
        )
        if not stem_names:
            stem_names = STEM_NAMES["htdemucs"]
        stem_urls = {}

        def save_stem(idx, name):
            stem_path = output_dir / f"{name}.wav"
            save_audio(sources[idx], stem_path, samplerate=demucs_model.samplerate)
            return idx, name, stem_path.name

        save_tasks = [
            asyncio.create_task(asyncio.to_thread(save_stem, idx, name))
            for idx, name in enumerate(stem_names)
            if idx < len(sources)
        ]
        total_stems = len(save_tasks)
        saved_stems = 0
        for completed in asyncio.as_completed(save_tasks):
            _, name, filename = await completed
            stem_urls[name] = f"/outputs/{output_dir.name}/{filename}"
            saved_stems += 1
            progress = 85 + int(saved_stems / total_stems * 10)
            yield sse_event(
                {"status": f"Saved {name} stem", "progress": progress}
            )

        # Keep the response stable even though parallel saves can finish in a
        # different order on each run.
        ordered_stem_urls = {
            name: stem_urls[name] for name in stem_names if name in stem_urls
        }
        elapsed = time.perf_counter() - start_time
        succeeded = True
        yield sse_event(
            {
                "stems": ordered_stem_urls,
                "progress": 100,
                "elapsed_seconds": round(elapsed, 1),
            }
        )

    except asyncio.CancelledError:
        raise
    except Exception as e:
        print(f"Error processing audio: {str(e)}")
        yield sse_event({"error": str(e)})
    finally:
        # Also covers decode failures and clients that disconnect mid-stream.
        shutil.rmtree(file_path.parent, ignore_errors=True)
        if not succeeded:
            shutil.rmtree(output_dir, ignore_errors=True)


@app.post("/separate-stream")
async def separate_audio_stream(
    audio: UploadFile = File(...),
    model: str = Query(default="htdemucs", regex="^(htdemucs|htdemucs_6s)$"),
    preview: bool = Query(default=False),
):
    safe_filename = Path(audio.filename or "audio").name
    if safe_filename in {"", ".", ".."}:
        raise HTTPException(status_code=400, detail="Invalid audio filename")

    is_audio_mime = bool(
        audio.content_type and audio.content_type.startswith("audio/")
    )
    is_audio_extension = Path(safe_filename).suffix.lower() in SUPPORTED_AUDIO_EXTENSIONS
    if not is_audio_mime and not is_audio_extension:
        raise HTTPException(status_code=400, detail="File must be an audio file")

    if model not in STEM_NAMES:
        raise HTTPException(status_code=400, detail=f"Unknown model: {model}")

    request_id = str(uuid.uuid4())
    request_upload_dir = UPLOAD_DIR / request_id
    request_output_dir = OUTPUT_DIR / request_id
    request_upload_dir.mkdir(exist_ok=True)
    request_output_dir.mkdir(exist_ok=True)
    file_path = request_upload_dir / safe_filename

    try:
        def persist_upload():
            total_bytes = 0
            with open(file_path, "wb") as buffer:
                while chunk := audio.file.read(1024 * 1024):
                    total_bytes += len(chunk)
                    if total_bytes > MAX_UPLOAD_BYTES:
                        raise UploadTooLargeError(
                            f"Audio files are limited to "
                            f"{MAX_UPLOAD_BYTES / 1024 / 1024:.0f} MB"
                        )
                    buffer.write(chunk)

        await asyncio.to_thread(persist_upload)
        await audio.close()

        return StreamingResponse(
            process_audio_streaming(file_path, request_output_dir, model, preview),
            media_type="text/event-stream",
            headers={
                "Cache-Control": "no-cache, no-transform",
                "X-Accel-Buffering": "no",
            },
        )

    except UploadTooLargeError as e:
        await audio.close()
        shutil.rmtree(request_upload_dir, ignore_errors=True)
        shutil.rmtree(request_output_dir, ignore_errors=True)
        raise HTTPException(status_code=413, detail=str(e))
    except HTTPException:
        await audio.close()
        shutil.rmtree(request_upload_dir, ignore_errors=True)
        shutil.rmtree(request_output_dir, ignore_errors=True)
        raise
    except Exception as e:
        await audio.close()
        shutil.rmtree(request_upload_dir, ignore_errors=True)
        shutil.rmtree(request_output_dir, ignore_errors=True)
        raise HTTPException(status_code=500, detail=str(e))


@app.get("/outputs/{request_id}/{filename}")
async def get_output_file(
    request_id: str,
    filename: str,
    format: str = Query(default="wav", regex="^(wav|mp3|flac)$"),
):
    try:
        uuid.UUID(request_id)
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid request ID")
    if Path(filename).name != filename:
        raise HTTPException(status_code=400, detail="Invalid filename")

    file_path = OUTPUT_DIR / request_id / filename
    if not file_path.exists():
        raise HTTPException(status_code=404, detail="File not found")

    # If requesting a different format, convert using ffmpeg
    if format != "wav":
        converted_name = file_path.stem + f".{format}"
        converted_path = file_path.parent / converted_name
        lock = conversion_locks.setdefault(converted_path, asyncio.Lock())
        async with lock:
            if not converted_path.exists():
                try:
                    cmd = [
                        "ffmpeg",
                        "-nostdin",
                        "-hide_banner",
                        "-loglevel",
                        "error",
                        "-i",
                        str(file_path),
                        "-y",
                    ]
                    if format == "mp3":
                        cmd += ["-codec:a", "libmp3lame", "-b:a", "320k"]
                    elif format == "flac":
                        cmd += ["-codec:a", "flac"]
                    cmd.append(str(converted_path))
                    await asyncio.to_thread(
                        subprocess.run,
                        cmd,
                        check=True,
                        capture_output=True,
                    )
                except Exception as e:
                    raise HTTPException(
                        status_code=500,
                        detail=(
                            f"Format conversion failed: {str(e)}. "
                            "Is ffmpeg installed?"
                        ),
                    )

        mime = {"mp3": "audio/mpeg", "flac": "audio/flac"}[format]
        return FileResponse(path=converted_path, media_type=mime, filename=converted_name)

    return FileResponse(path=file_path, media_type="audio/wav", filename=filename)


@app.get("/gpu-status")
async def gpu_status():
    """Detailed GPU information for the frontend."""
    info = {
        "cuda_available": torch.cuda.is_available(),
        "device": device,
        "gpu_name": None,
        "gpu_memory_mb": None,
        "torch_version": torch.__version__,
        "cuda_version": None,
    }
    if torch.cuda.is_available():
        try:
            info["gpu_name"] = torch.cuda.get_device_name(0)
            info["gpu_memory_mb"] = round(
                torch.cuda.get_device_properties(0).total_mem / 1024 / 1024
            )
            info["cuda_version"] = torch.version.cuda
        except Exception:
            pass
    return info


@app.delete("/cleanup/{request_id}")
async def cleanup_files(request_id: str):
    try:
        uuid.UUID(request_id)
    except ValueError:
        raise HTTPException(status_code=400, detail="Invalid request ID")

    upload_dir = UPLOAD_DIR / request_id
    output_dir = OUTPUT_DIR / request_id
    shutil.rmtree(upload_dir, ignore_errors=True)
    shutil.rmtree(output_dir, ignore_errors=True)
    return {"status": "cleaned up"}


def handle_shutdown(signum, frame):
    print(f"\nReceived signal {signum}, shutting down gracefully...")
    shutil.rmtree(temp_base, ignore_errors=True)
    sys.exit(0)


if __name__ == "__main__":
    import uvicorn

    parser = argparse.ArgumentParser(description="Stem Separator Backend")
    parser.add_argument(
        "--port",
        type=int,
        default=int(os.environ.get("PORT", "8000")),
        help="Port to listen on",
    )
    parser.add_argument(
        "--host",
        type=str,
        default=os.environ.get("HOST", "127.0.0.1"),
        help="Host to bind to",
    )
    args = parser.parse_args()

    signal.signal(signal.SIGINT, handle_shutdown)
    signal.signal(signal.SIGTERM, handle_shutdown)

    print(f"Starting Stem Separator Backend on {args.host}:{args.port}")
    uvicorn.run(app, host=args.host, port=args.port)
