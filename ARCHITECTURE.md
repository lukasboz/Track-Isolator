# Local-only architecture

## 1. System boundaries

The repository contains one processing engine and two user interfaces.

```text
┌──────────────────────┐
│ Windows WPF client   │
└──────────┬───────────┘
           │
           │ multipart upload, SSE progress, WAV download
           │
┌──────────▼───────────┐        ┌──────────────────────┐
│ Local FastAPI server │◄───────┤ Browser client       │
│ 127.0.0.1            │        │ Next.js + Web Audio  │
└──────────┬───────────┘        └──────────────────────┘
           │
           │ FFmpeg decode, normalization, inference
           │
┌──────────▼───────────┐
│ Demucs htdemucs      │
│ CUDA, MPS, or CPU    │
└──────────┬───────────┘
           │
           ▼
    PCM-16 WAV stems
```

There is no cloud processing path. The repository contains no operational code
for authentication, billing, databases, object storage, hosted GPU workers, or
worker callbacks.

## 2. Shared backend

The backend is implemented in `backend/app.py`.

Responsibilities:

- Select the processing device.
- Load and cache Demucs models.
- Validate filenames, extensions, and upload size.
- Persist uploads without blocking the event loop.
- Decode audio with FFmpeg through Demucs/Torchaudio.
- Normalize the signal according to the Demucs reference process.
- Serialize inference to control memory usage.
- write stems concurrently.
- stream progress to clients.
- serve output files.
- clean temporary files.

### Device selection

When `STEM_SEPARATOR_DEVICE=auto`, the backend checks:

1. CUDA
2. Apple MPS
3. CPU

The packaged Windows release currently contains CPU PyTorch. Development
environments can install a CUDA-enabled PyTorch build with
`backend/scripts/install-cuda-torch.ps1`.

### Model lifecycle

`htdemucs` is loaded during application startup. Loaded models are retained in
a process-level dictionary so repeated jobs do not reconstruct the model.

Release builds copy the model YAML and checkpoint into `backend/models/`.
PyInstaller packages this repository in the backend distribution, eliminating
the first-run model download.

### Inference settings

The local backend uses:

- Model: `htdemucs`
- Shifts: `1`
- Split inference: enabled
- Overlap: `0.25`
- Output sample rate: model-native
- Output encoding: PCM WAV

Inference runs under one process-wide asynchronous lock. Multiple callers can
upload or download concurrently, but only one Demucs inference runs at a time.
This prevents GPU-memory contention and CPU oversubscription.

## 3. Backend API

### `GET /health`

Reports service identity, device selection, model state, and available models.

Example:

```json
{
  "status": "healthy",
  "service": "stem-separator-local",
  "device": "cpu",
  "gpu_name": null,
  "model_loaded": true,
  "available_models": ["htdemucs", "htdemucs_6s"]
}
```

### `POST /separate-stream`

Query parameters:

| Name | Default | Meaning |
| --- | --- | --- |
| `model` | `htdemucs` | `htdemucs` or `htdemucs_6s` |
| `preview` | `false` | Process only the first 30 seconds |

Multipart field:

```text
audio
```

The response is `text/event-stream`. Each event contains JSON:

```text
data: {"status":"Separating audio...","progress":50}

data: {"stems":{"vocals":"/outputs/<uuid>/vocals.wav",...},"progress":100}
```

Errors are returned inside the stream:

```text
data: {"error":"error message"}
```

### `GET /outputs/{request_id}/{filename}`

Returns a generated output. `request_id` must be a UUID and `filename` may not
contain path components.

The optional `format` query accepts:

```text
wav, mp3, flac
```

The two clients use the default WAV output.

### `DELETE /cleanup/{request_id}`

Deletes backend upload and output directories for one request.

### `GET /gpu-status`

Returns detailed Torch, CUDA, and GPU information for diagnostics.

## 4. Request lifecycle

1. A client checks `/health`.
2. The client sends one multipart audio file.
3. The backend validates the extension and enforces the 50 MB maximum.
4. The upload is copied to an OS temporary directory.
5. Demucs decodes and resamples the input.
6. The uploaded source file is deleted.
7. The input is normalized.
8. Inference waits for and enters the process-wide inference lock.
9. The output is denormalized.
10. Each stem is written concurrently.
11. Relative output URLs are sent through SSE.
12. The client plays or downloads the output.
13. The client calls the cleanup endpoint.

If decoding, inference, output writing, or the client connection fails, the
backend removes incomplete request directories.

## 5. Temporary storage

Backend temporary data:

```text
<OS temp>/stem-separator/uploads/<request UUID>/
<OS temp>/stem-separator/outputs/<request UUID>/
```

Windows client copies:

```text
<OS temp>/StemSeparator/<request UUID>/
```

The Windows client deletes both the server outputs and its local copies when
the user resets. It also removes local copies during shutdown.

The browser streams directly from the backend and requests cleanup when the
result is replaced, reset, or the page unloads.

## 6. Browser security

The backend defaults to these allowed origins:

```text
http://127.0.0.1:3000
http://localhost:3000
```

Override them with a comma-separated list:

```text
STEM_SEPARATOR_ALLOWED_ORIGINS=https://example.vercel.app
```

The backend also emits the Private Network Access response header needed by
compatible browsers when an HTTPS page calls a loopback service.

The backend should remain bound to `127.0.0.1`. Origin checks are an additional
browser boundary, not a replacement for network isolation.

## 7. Windows packaging

The Windows package has two layers:

```text
StemSeparator.exe
backend/
  stem-separator-backend.exe
  _internal/
    models/
    ffmpeg.exe
    ffprobe.exe
    PyTorch and Python runtime files
```

The WPF application is a self-contained .NET 8 single-file executable. The
backend remains a one-directory PyInstaller bundle because PyTorch and its
native libraries must be available as real files.

At startup, the Windows client:

1. Reuses a compatible backend already running on port 8000, if present.
2. Otherwise finds a free loopback port.
3. launches `backend/stem-separator-backend.exe`.
4. passes the selected port through environment and command-line arguments.
5. polls `/health` for up to 120 seconds.

## 8. Web deployment

The Next.js project contains only statically prerendered application routes.
It has no API routes.

A hosted deployment serves JavaScript, CSS, fonts, and HTML. The browser—not
the hosting provider—calls the local backend. Consequently:

- The backend must be running on the same computer as the browser.
- A phone cannot use another computer's loopback service.
- The browser may require local-network permission.
- The backend origin allowlist must include the hosted site.

## 9. Operational limitations

- CPU processing is compute-intensive and may take several minutes.
- The packaged Windows release uses CPU inference unless rebuilt with CUDA
  PyTorch.
- Browser memory limits affect playback and mix export because decoded stems
  are held in Web Audio buffers.
- The 50 MB input cap limits upload memory and processing cost, not duration.
- Only one inference executes per backend process.
- The applications are currently unsigned, so Windows SmartScreen may warn on
  first launch.

## 10. Technical documentation

- [Web application](./docs/WEB_TECHNICAL.md)
- [Windows application](./docs/WINDOWS_TECHNICAL.md)
