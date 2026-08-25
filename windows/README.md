# Stem Separator for Windows

This folder contains the native local-only Windows client.

## Run the built application

Use either the extracted release directory:

```text
windows\build\release\StemSeparator.exe
```

or the portable archive:

```text
windows\build\StemSeparator-Windows-x64.zip
```

For the ZIP:

1. Extract the entire archive.
2. Keep `StemSeparator.exe` and the `backend` directory together.
3. Double-click `StemSeparator.exe`.
4. Wait for the local engine to load.
5. Choose or drop an audio file.
6. Click Start Separation.

The release includes .NET, Python, PyTorch, Demucs model data, FFmpeg, and
FFprobe. No separate runtime installation is required.

The application is unsigned, so Windows SmartScreen may display a warning.

## Build

Requirements:

- Windows x64
- .NET 8 SDK
- Python 3.10
- PowerShell
- FFmpeg and FFprobe on `PATH`

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File windows\scripts\build-release.ps1
```

Outputs:

```text
windows\build\release\
windows\build\StemSeparator-Windows-x64.zip
```

## Development

Open:

```text
windows\StemSeparator.sln
```

The app will:

1. reuse a compatible backend on port 8000, or
2. launch a packaged development backend, or
3. run `backend/app.py` with the backend virtual environment.

## Documentation

- [Comprehensive Windows documentation](../docs/WINDOWS_TECHNICAL.md)
- [Shared architecture](../ARCHITECTURE.md)
