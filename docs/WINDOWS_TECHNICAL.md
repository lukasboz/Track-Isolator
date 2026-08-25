# Windows application technical documentation

## 1. Purpose

`windows/` contains the native Windows client for Stem Separator.

It provides:

- Local audio-file selection and drag-and-drop.
- Automatic local backend startup.
- Separation progress and cancellation.
- Concurrent stem download.
- Synchronized multistem playback.
- Per-stem mute and volume.
- Individual stem export.
- Custom WAV mix export.
- Karaoke export.
- Keyboard transport controls.

The application has no cloud-processing path and does not require an account,
database, storage provider, or billing service.

## 2. Technology stack

| Layer | Technology |
| --- | --- |
| Runtime | .NET 8 |
| UI | WPF and XAML |
| Architecture | MVVM |
| MVVM source generation | CommunityToolkit.Mvvm |
| Playback and mixing | NAudio |
| Backend client | `HttpClient` |
| Progress transport | Server-Sent Events |
| Backend package | PyInstaller one-directory bundle |
| Release target | Windows x64 |

## 3. Project layout

```text
windows/
├── StemSeparator.sln
├── StemSeparator/
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── Models/
│   ├── Services/
│   ├── Utilities/
│   ├── ViewModels/
│   ├── Views/
│   ├── Themes/
│   ├── Converters/
│   ├── Resources/
│   └── StemSeparator.csproj
├── scripts/
│   ├── build-release.ps1
│   └── bundle-backend.ps1
└── build/
    ├── release/
    └── StemSeparator-Windows-x64.zip
```

`bin/`, `obj/`, and `build/` are generated outputs.

## 4. Application lifecycle

`App.xaml` creates `Views/MainWindow.xaml`.

`App.xaml.cs` owns one process-wide `MainViewModel` and handles:

- Startup initialization.
- Dispatcher exceptions.
- Unhandled application-domain exceptions.
- Unobserved task exceptions.
- Shutdown cleanup.

### Startup

`OnStartup` calls:

```text
MainViewModel.InitializeAsync()
```

The window can be displayed while the state is `Starting`. The view model
changes to `Idle` only after the backend reports healthy.

### Shutdown

`MainViewModel.Shutdown()`:

1. cancels active processing.
2. disposes playback.
3. removes downloaded temporary stems.
4. disposes the HTTP client.
5. stops the backend process if this application started it.

If the application reused an externally managed backend on port 8000, it does
not terminate that process.

## 5. Application state machine

`Models/AppState.cs` defines:

```text
Starting
Idle
Uploading
Processing
Ready
Error
```

The main window maps states to views using
`StateToVisibilityConverter`.

```text
Starting ──healthy──> Idle
Idle ──process──> Uploading
Uploading ──SSE separating status──> Processing
Processing ──complete──> Ready
Uploading/Processing ──cancel──> Idle
Any active state ──failure──> Error
Ready ──reset──> Idle
Error ──dismiss──> Idle
```

## 6. Main view model

`ViewModels/MainViewModel.cs` coordinates the application.

### Owned services

- `BackendProcessManager`
- `BackendApiClient`
- `StemPlayerViewModel`
- Active `CancellationTokenSource`
- Active `SeparationResult`

### File selection

Both the file dialog and drag-and-drop call the same validation path.

Validation checks:

- File exists.
- Extension is supported.
- File is non-empty.
- File is no larger than 50 MB.

An invalid file moves the application to `Error`.

### Processing

The Process command:

1. revalidates the selected file.
2. creates a cancellation token source.
3. moves to `Uploading`.
4. streams the multipart file to the backend.
5. consumes SSE status and percentage events.
6. moves to `Processing` when the backend reports separation.
7. downloads all stems concurrently.
8. validates that all four expected stems exist.
9. creates the player.
10. moves to `Ready`.

### Cancellation

The Cancel command cancels the HTTP request. The backend detects the abandoned
stream and deletes incomplete output through its generator cleanup path.

### Reset

Reset:

- Disposes the player.
- Calls backend cleanup.
- Deletes client-side temporary copies.
- Clears the selected file.
- Returns to `Idle`.

## 7. Backend process manager

`Services/BackendProcessManager.cs` controls the local engine.

### Startup resolution order

1. Check for a compatible healthy backend on port 8000.
2. Find a free port beginning at 8000.
3. Look for the packaged backend:

   ```text
   <application directory>/backend/stem-separator-backend.exe
   ```

4. Look for a development PyInstaller build:

   ```text
   backend/pyinstaller/dist/stem-separator-backend/
   ```

5. Look for the repository's `backend/app.py` and run it with:

   ```text
   backend/venv/Scripts/python.exe
   ```

   or system `python`.

The former legacy backend fallback has been removed.

### Dynamic port

The manager scans ports 8000 through 8099. The selected port is passed through:

```text
PORT
HOST=127.0.0.1
```

The Python fallback also receives explicit `--host` and `--port` arguments.

### Health polling

The process is polled every 500 milliseconds for up to 120 seconds. This
timeout accounts for loading the Demucs model.

If the child process exits early, startup fails immediately with its exit code.

### Output handling

Standard output and error are redirected and written to the Visual Studio
debug stream. Redirecting both streams also prevents a child-process pipe from
blocking when its buffer fills.

## 8. HTTP and SSE client

`Services/BackendApiClient.cs` owns one `HttpClient` configured for the selected
backend port.

### Upload

The source file is opened as a stream and wrapped in
`MultipartFormDataContent`.

The request uses:

```text
POST /separate-stream
HttpCompletionOption.ResponseHeadersRead
```

This avoids buffering the full SSE response.

### SSE parsing

`Services/SseClient.cs` reads the response line by line and deserializes
`data:` records into `Models/SSEEvent`.

Recognized fields:

- `status`
- `progress`
- `stems`
- `error`

Malformed JSON events are ignored. Backend error events become client
exceptions.

### Request validation

The client expects output paths in this form:

```text
/outputs/{UUID}/{filename}
```

It verifies the `outputs` prefix and UUID before creating its local directory.

### Stem download

Each known stem is downloaded concurrently with
`HttpCompletionOption.ResponseHeadersRead`.

Downloads are copied asynchronously in 1 MB blocks to:

```text
%TEMP%\StemSeparator\<request UUID>\
```

The completed result must contain every `StemType` value:

- Vocals
- Drums
- Bass
- Other

### Cleanup

Cleanup calls the backend endpoint and deletes the local result directory in a
`finally` block. Local files are therefore removed even if the backend is
already unavailable.

## 9. Playback architecture

`Services/AudioPlaybackService.cs` uses NAudio.

### Load

For each stem:

1. create an `AudioFileReader`.
2. wrap it in a `VolumeSampleProvider`.
3. add the provider to a `MixingSampleProvider`.

Every generated stem has the same sample rate and channel layout because they
come from one Demucs inference.

The mixer uses `ReadFully = true`, keeping the output active until the longest
stem completes.

### Output

A `WaveOutEvent` plays the mixed sample provider.

### Synchronization

All stem readers feed one mixer and one output device, so they share the same
audio clock.

### Seeking

Seeking assigns the same `CurrentTime` to every reader.

### Volume and mute

Each `StemViewModel` owns the UI volume and pre-mute volume. Muting sets the
corresponding provider to zero. Unmuting restores the view model's saved
volume.

Mute state is intentionally managed in one layer. The previous duplicate
service-level mute state was removed because it could save zero as the
pre-mute value.

### Position updates

A WPF `DispatcherTimer` emits the current reader position every 50
milliseconds.

## 10. Player view model

`ViewModels/StemPlayerViewModel.cs` maps audio service operations to commands
and observable properties.

Properties include:

- Playing state
- Current time
- Duration
- Formatted current time
- Formatted duration
- Observable stem collection

Commands include:

- Play or pause
- Download mix
- Karaoke mode

## 11. Stem view model

Each `StemViewModel` contains:

- Stem enum
- Display name
- Local WAV path
- Mute state
- Linear volume from `0.0` to `1.0`
- Slider-facing volume from `0` to `100`

The Download command copies the local WAV to a user-selected path.

## 12. Mix export

Mix export opens fresh readers so the operation does not disturb playback.

For every unmuted stem:

1. create an `AudioFileReader`.
2. add it to a new `MixingSampleProvider`.
3. write PCM-16 WAV with `WaveFileWriter.CreateWaveFile16`.
4. dispose all temporary readers.

Karaoke mode mutes vocals, unmutes the other stems, and invokes the same mix
export path.

## 13. Keyboard controls

`Views/MainWindow.xaml.cs` handles shortcuts only in `Ready` state.

| Key | Action |
| --- | --- |
| Space | Play or pause |
| 1 | Toggle vocals |
| 2 | Toggle drums |
| 3 | Toggle bass |
| 4 | Toggle other |
| Left Arrow | Seek backward five seconds |
| Right Arrow | Seek forward five seconds |
| M | Mute every stem |
| U | Unmute every stem |

## 14. Themes and views

The application merges:

- `Themes/Colors.xaml`
- `Themes/DarkTheme.xaml`
- `Themes/Controls.xaml`

Main views:

| View | Purpose |
| --- | --- |
| `MainWindow` | State host, header, errors, keyboard controls |
| `FileUploadView` | File dialog and drag-and-drop |
| `ProcessingView` | Spinner, progress, and cancellation |
| `StemPlayerView` | Transport, stems, export, and reset |

## 15. Release package

The published layout is:

```text
windows/build/release/
├── StemSeparator.exe
└── backend/
    ├── stem-separator-backend.exe
    └── _internal/
        ├── models/
        ├── ffmpeg.exe
        ├── ffprobe.exe
        ├── torch runtime
        └── Python runtime
```

`StemSeparator.exe` is a self-contained .NET single-file application.

The backend is intentionally a directory. PyTorch, FFmpeg, the Python runtime,
and the Demucs checkpoint rely on native files that PyInstaller extracts or
loads from the `_internal` tree.

## 16. Running the release

### Portable ZIP

Use:

```text
windows/build/StemSeparator-Windows-x64.zip
```

1. Copy the ZIP to a Windows x64 computer.
2. Extract the entire archive.
3. Keep `StemSeparator.exe` and `backend/` together.
4. Double-click `StemSeparator.exe`.
5. Wait for the Starting screen to finish loading the model.
6. Select or drop an audio file.
7. Click Start Separation.

No Python or .NET installation is required for this release.

### Direct build directory

From the repository:

```text
windows\build\release\StemSeparator.exe
```

Do not move only the EXE. The backend folder is required.

### SmartScreen

The current build is unsigned. Windows may show a SmartScreen warning. For a
build you trust, select More info and Run anyway. A production distribution
should be code-signed.

## 17. Building the release

### Requirements

- Windows x64
- .NET 8 SDK
- Python 3.10
- PowerShell
- FFmpeg and FFprobe available on `PATH`
- Internet access for initial NuGet, Python, and model downloads

### Command

From the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File windows\scripts\build-release.ps1
```

### Backend build

`backend/scripts/build-backend-windows.ps1`:

1. creates `backend/venv` if needed.
2. installs CPU PyTorch and backend dependencies.
3. copies FFmpeg and FFprobe into the bundle input.
4. downloads and copies the `htdemucs` checkpoint.
5. runs PyInstaller.

Backend output:

```text
backend/pyinstaller/dist/stem-separator-backend/
```

### WPF publish

The release script runs:

```powershell
dotnet publish windows\StemSeparator\StemSeparator.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

Trimming is disabled because WPF, source-generated MVVM properties, and NAudio
use runtime metadata that should not be removed without dedicated trim
testing.

### Archive

The release directory is compressed to:

```text
windows/build/StemSeparator-Windows-x64.zip
```

## 18. CPU and CUDA builds

The standard release packages CPU PyTorch for maximum compatibility.

For development on a supported NVIDIA system:

```powershell
backend\scripts\install-cuda-torch.ps1
```

Then run the application from source or rebuild the backend. CUDA release
bundles are much larger and should be tested on the intended driver/runtime
combination.

Use this endpoint to confirm the selected device:

```text
http://127.0.0.1:<port>/gpu-status
```

## 19. Failure handling

### Backend startup failure

The UI moves to `Error` if:

- The backend executable is missing.
- Python fallback is unavailable.
- The process exits before health succeeds.
- Model loading exceeds 120 seconds.

### Separation failure

An SSE error becomes an exception and is displayed in the error banner.

### Unhandled UI failure

Dispatcher and task exceptions are caught by `App.xaml.cs` and converted to an
error state where possible.

### Cleanup failure

Backend cleanup failures are logged but do not prevent local temporary-file
cleanup.

## 20. Troubleshooting

### The application remains on Starting

- Wait up to two minutes on slower disks.
- Check Windows Defender quarantine or Controlled Folder Access.
- Confirm that the full `backend/` directory exists beside the EXE.
- Run `backend/stem-separator-backend.exe` from a terminal to inspect errors.

### Backend executable disappears

Antivirus software may quarantine unsigned PyInstaller applications. Restore
the file only if you trust the build, or code-sign the release.

### Audio fails to decode

Confirm these files exist:

```text
backend/_internal/ffmpeg.exe
backend/_internal/ffprobe.exe
```

### No sound

- Check the default Windows output device.
- Verify at least one stem is unmuted and has nonzero volume.
- Confirm the WAV files exist in `%TEMP%\StemSeparator`.

### Separation is slow

The packaged release is CPU-only. Use a CUDA-enabled development or release
backend for supported NVIDIA hardware.

### Port conflict

The application reuses a healthy backend on port 8000 or scans through port
8099. A non-backend service on port 8000 is ignored unless it returns the
expected healthy JSON.

## 21. Release verification checklist

- `dotnet build -c Release` has zero warnings and zero errors.
- `StemSeparator.exe` exists.
- The sibling backend directory exists.
- The frozen backend starts.
- `/health` reports `model_loaded: true`.
- Starting the WPF EXE starts the backend.
- Closing the WPF EXE stops its managed backend.
- A WAV test file completes separation.
- Playback, mute, seek, stem download, mix export, and reset are tested.
- The final package is code-signed for public distribution.
