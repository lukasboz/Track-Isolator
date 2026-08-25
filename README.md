# Stem Separator

Stem Separator is a local-only music stem separation application with two
clients:

- A native Windows desktop application.
- A browser application built with Next.js.

Both clients use the same FastAPI and Demucs backend in `backend/`. Audio is
processed on the user's computer and is never sent to a hosted inference
service, object store, database, billing provider, or account system.

The former macOS client and the former cloud-processing web implementation have
been removed.

## Architecture

```text
Windows WPF client ─┐
                    ├── HTTP + Server-Sent Events ──> FastAPI ──> Demucs
Browser client ─────┘                                  │
                                                      └── local WAV stems
```

The backend:

- Binds to loopback by default.
- Loads the `htdemucs` model once and reuses it.
- Selects CUDA, Apple MPS, or CPU when supported by the installed PyTorch
  build.
- Accepts files up to 50 MB.
- Streams status and percentage updates.
- Writes PCM WAV stems to the operating-system temporary directory.
- Removes uploads after decoding and removes outputs on cleanup.

See [ARCHITECTURE.md](./ARCHITECTURE.md) for the shared design.

## Repository layout

| Path | Purpose |
| --- | --- |
| `backend/` | Shared FastAPI, PyTorch, Torchaudio, FFmpeg, and Demucs engine |
| `backend/pyinstaller/` | Windows backend packaging configuration |
| `backend/scripts/` | Backend setup, model caching, and build scripts |
| `website-version/` | Local-only Next.js browser client |
| `windows/` | Native .NET 8 WPF client |
| `docs/WEB_TECHNICAL.md` | Comprehensive browser-client documentation |
| `docs/WINDOWS_TECHNICAL.md` | Comprehensive Windows-client documentation |

## Run the prebuilt Windows application

The built package is located at:

```text
windows/build/release/
```

Run:

```text
windows/build/release/StemSeparator.exe
```

The sibling `backend/` directory is required. Do not copy
`StemSeparator.exe` by itself.

A portable archive is also available:

```text
windows/build/StemSeparator-Windows-x64.zip
```

Extract the whole ZIP, then double-click `StemSeparator.exe`. The application
starts and stops its local backend automatically. The release is self-contained
and does not require a separate .NET installation.

See [windows/README.md](./windows/README.md) for detailed run instructions and
[docs/WINDOWS_TECHNICAL.md](./docs/WINDOWS_TECHNICAL.md) for internals.

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

## Verification performed

The current source was verified with:

- Windows Release compilation with zero warnings and zero errors.
- Web ESLint.
- Web TypeScript type checking.
- Next.js production build.
- `npm audit` with zero known vulnerabilities.
- PyInstaller backend startup with the bundled model.
- Backend `/health` response from the frozen executable.
- Final `StemSeparator.exe` startup.
- Automatic launch and shutdown of the bundled backend.

## Privacy and security boundary

- The backend listens on loopback unless explicitly configured otherwise.
- The browser client has no authentication, database, payments, cloud jobs, or
  upload API.
- Browser origins are restricted through
  `STEM_SEPARATOR_ALLOWED_ORIGINS`.
- Temporary output identifiers are UUIDs.
- Output and cleanup routes reject invalid identifiers and path components.
- Uploaded files are removed immediately after decode.

Do not expose the backend port to a public network.
