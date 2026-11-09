# Track Isolator

rack Isolator is a local-only music stem separation application with two
clients:

- A native Windows desktop application.
- A browser application built with Next.js.

Both clients use the same FastAPI and Demucs backend in `backend/`. Audio is
processed on the user's computer and is never sent to a hosted inference
service, object store, database, billing provider, or account system.

## Run the web application

### 1. Start the backend

FFmpeg and FFprobe must be available on `PATH`.

Windows PowerShell:

```powershell
py -m venv backend\venv
backend\venv\Scripts\Activate.ps1
python -m pip install -r backend\requirements.txt
python backend\app.py --host 127.0.0.1 --port 8000
```

macOS or Linux:

```bash
python3 -m venv backend/.venv
source backend/.venv/bin/activate
python -m pip install -r backend/requirements.txt
python backend/app.py --host 127.0.0.1 --port 8000
```

### 2. Start the browser client

Node.js 22 is recommended.

```bash
cd website-version
npm ci
npm run dev
```

Open `http://localhost:3000`.

The page talks directly to `http://127.0.0.1:8000`. Next.js does not proxy,
store, or process audio.

See [website-version/README.md](./website-version/README.md) and
[docs/WEB_TECHNICAL.md](./docs/WEB_TECHNICAL.md).

## Build the Windows release

Requirements:

- Windows x64
- .NET 8 SDK
- Python 3.10
- PowerShell
- FFmpeg and FFprobe
- Internet access during dependency and model setup

Run from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File windows\scripts\build-release.ps1
```

The script:

1. Creates or updates the backend virtual environment.
2. caches the Demucs model.
3. packages the backend with PyInstaller.
4. publishes a self-contained single-file WPF executable.
5. copies the backend beside the application.
6. creates the portable ZIP.

## Supported inputs and outputs

Accepted input extensions:

```text
MP3, WAV, FLAC, OGG, M4A, AAC, AIF, AIFF
```

Maximum input size:

```text
50 MB
```

Default outputs:

```text
vocals.wav
drums.wav
bass.wav
other.wav
```

The backend also contains support for `htdemucs_6s`, but the two clients
intentionally expose the standard four-stem model.
