# Web application technical documentation

## 1. Purpose

`website-version/` is a local-processing-only browser client for the shared
FastAPI and Demucs backend.

The website does not contain:

- Authentication
- User accounts
- Server-side audio upload routes
- Job records
- A database
- Object storage
- Billing
- Cloud GPU integration
- Worker callbacks

The hosting server delivers the interface. The user's browser transfers audio
directly to a backend bound to the loopback interface on the same computer.

## 2. Technology stack

| Layer | Technology |
| --- | --- |
| Framework | Next.js 16 |
| UI | React 19 |
| Language | TypeScript |
| Styling | Tailwind CSS 3 |
| Playback | Web Audio API |
| Build | Next.js production build |
| Backend transport | Fetch, multipart form data, streamed SSE |

Node.js 22 is the supported development runtime.

## 3. Source layout

```text
website-version/
├── src/
│   ├── app/
│   │   ├── layout.tsx
│   │   ├── page.tsx
│   │   ├── local/page.tsx
│   │   ├── globals.css
│   │   └── fonts/
│   ├── components/app/
│   │   ├── LocalSeparator.tsx
│   │   ├── StemPlayer.tsx
│   │   ├── StemRow.tsx
│   │   └── DownloadMix.tsx
│   └── lib/
│       └── local-backend.ts
├── eslint.config.mjs
├── next.config.mjs
├── package.json
├── tailwind.config.ts
└── tsconfig.json
```

Generated directories such as `.next/` and `node_modules/` are not source
files and are ignored.

## 4. Routes

### `/`

The main application. `src/app/page.tsx` renders `LocalSeparator`.

### `/local`

A compatibility alias that redirects to `/`.

### `/_not-found`

The framework-provided not-found route.

There are no `/api/*` routes.

## 5. Local backend module

`src/lib/local-backend.ts` contains the browser's complete backend contract.

### Exported types

```ts
type StemName = "vocals" | "drums" | "bass" | "other";
type StemUrls = Record<StemName, string>;
```

Other exported interfaces represent:

- Backend health
- Separation progress
- Completed separation results

### Constants

```text
Maximum file size: 50 MB
Default backend: http://127.0.0.1:8000
Model requested: htdemucs
```

Supported extensions:

```text
.mp3 .wav .flac .ogg .m4a .aac .aif .aiff
```

### `getBackendHealth`

Calls `/health` with `cache: no-store`. A successful response must use
`status: "healthy"`.

### `separateAudioLocally`

1. Validates the file extension, size, and non-empty content.
2. creates a `FormData` body with the `audio` field.
3. sends `POST /separate-stream`.
4. reads the response as a byte stream.
5. decodes UTF-8 incrementally.
6. normalizes CRLF event delimiters.
7. parses `data:` lines as JSON.
8. reports status and progress to the UI.
9. verifies all four expected stems.
10. converts relative output paths to absolute loopback URLs.
11. extracts the request UUID for cleanup.

An `AbortSignal` is mandatory. Canceling it closes the fetch and causes the
backend's generator cleanup path to remove incomplete output.

### `cleanupRequest`

Sends:

```text
DELETE /cleanup/{request UUID}
```

The unload path uses Fetch `keepalive` as a best-effort cleanup request.

## 6. Main application state

`LocalSeparator.tsx` owns the application workflow.

Important state values:

| State | Meaning |
| --- | --- |
| `health` | Local engine and device information |
| `checking` | Health request in progress |
| `file` | Selected browser file |
| `preview` | Whether to limit processing to 30 seconds |
| `processing` | Separation request is active |
| `progress` | Backend-reported percentage |
| `statusText` | Backend-reported status |
| `error` | User-facing validation or runtime failure |
| `stemUrls` | Four completed local output URLs |

Refs are used for resources that must survive render changes:

- File input element
- Active `AbortController`
- Current backend request UUID

### Startup

On mount, the component performs a health request. If the backend is offline,
the page displays a startup command and a manual recheck button.

### Selection

The browser `File` object is validated immediately. Invalid files do not enable
the separation button.

### Processing

Before starting a new request, the previous request is cleaned up. During
processing:

- The file input and preview option are disabled.
- The action button becomes Cancel.
- SSE status updates are rendered.
- The progress bar follows the backend percentage.

### Completion

The returned stem map is passed to `StemPlayer`. The user can then reset the
page, which stops playback and deletes the backend output.

### Unmount

The active request is aborted and completed output is scheduled for cleanup.

## 7. Synchronized playback

`StemPlayer.tsx` uses one `AudioContext` for all four stems.

This avoids the drift associated with starting four independent HTML media
elements.

### Loading

The component:

1. fetches all four WAV files concurrently.
2. decodes them into `AudioBuffer` objects.
3. creates one `GainNode` per stem.
4. connects every gain node to the shared context destination.
5. uses the longest decoded duration for the transport range.

### Starting

Every `AudioBufferSourceNode` is scheduled for the same future
`AudioContext.currentTime` value. This gives every stem the same playback
clock.

### Pausing and seeking

Buffer source nodes are one-use objects. Pausing or seeking therefore:

1. calculates the current position from the context clock.
2. stops and disconnects existing source nodes.
3. stores the new offset.
4. creates a fresh set of sources when playback resumes.

### Muting

Muting changes the corresponding `GainNode` target between `1` and `0` with a
short smoothing constant to avoid abrupt clicks.

### Position display

`requestAnimationFrame` reads the shared audio clock and updates the transport
position while playback is active.

## 8. Browser-side mix export

`DownloadMix.tsx` creates a custom WAV mix from every unmuted stem.

The implementation:

1. fetches selected stems concurrently.
2. decodes them with an `AudioContext`.
3. creates an `OfflineAudioContext`.
4. starts all sources at offset zero.
5. renders the mixed buffer.
6. converts floating-point samples to interleaved PCM-16.
7. constructs a RIFF/WAVE header.
8. downloads the generated Blob.

Mix generation is entirely local. Long tracks can require substantial browser
memory because the encoded data, decoded buffers, rendered mix, and WAV output
can overlap in memory.

## 9. Configuration

### Browser backend URL

Optional build-time variable:

```text
NEXT_PUBLIC_LOCAL_BACKEND_URL=http://127.0.0.1:8000
```

If omitted, the same value is used by default.

Because this is a `NEXT_PUBLIC_` value, it is embedded in the browser bundle.
It is not a secret.

### Backend origin allowlist

For local development, no backend configuration is needed. The default
allowlist contains:

```text
http://localhost:3000
http://127.0.0.1:3000
```

For a hosted interface:

Windows PowerShell:

```powershell
$env:STEM_SEPARATOR_ALLOWED_ORIGINS="https://your-site.vercel.app"
python backend\app.py --host 127.0.0.1 --port 8000
```

macOS or Linux:

```bash
export STEM_SEPARATOR_ALLOWED_ORIGINS="https://your-site.vercel.app"
python backend/app.py --host 127.0.0.1 --port 8000
```

Multiple origins are comma-separated.

## 10. Development setup

### Requirements

- Node.js 22
- npm
- Python 3.10
- FFmpeg and FFprobe
- Backend Python dependencies

### Install

From `website-version/`:

```bash
npm ci
```

### Start

Terminal 1, repository root:

```bash
python backend/app.py --host 127.0.0.1 --port 8000
```

Terminal 2:

```bash
cd website-version
npm run dev
```

Open:

```text
http://localhost:3000
```

## 11. Quality commands

```bash
npm run lint
npm run typecheck
npm run build
npm audit
```

Expected route output from the production build:

```text
/
/_not-found
/local
```

All routes are statically prerendered.

## 12. Production deployment

The interface can be deployed to Vercel or another Next.js host.

The deployment does not need:

- Environment secrets
- A database
- Storage credentials
- Payment credentials
- A Python runtime
- GPU infrastructure

It only serves the interface. Every user must run the backend on the same
computer as the browser.

Detailed instructions are in
`website-version/VERCEL_FREE_DEPLOYMENT.md`.

## 13. Privacy and security

- Audio requests target loopback directly.
- There are no web API upload endpoints.
- There is no persistent browser job history.
- The host never receives audio through application code.
- CORS is restricted to configured origins.
- Backend paths use request UUIDs.
- Cleanup identifiers are validated server-side.

Do not change the backend host to `0.0.0.0` unless the network exposure has
been explicitly designed and secured.

## 14. Browser limitations

- Some managed browsers block public-site access to loopback services.
- Private Network Access behavior differs across browser versions.
- A hosted page must be included in the backend origin allowlist.
- Mobile devices cannot use a backend on another machine through
  `127.0.0.1`.
- Browser memory may limit long-track playback and mix export.
- Background-tab throttling can reduce UI update frequency, though the shared
  Web Audio playback clock remains authoritative.

## 15. Troubleshooting

### Local engine offline

Open this URL in the same browser:

```text
http://127.0.0.1:8000/health
```

If it fails, start the backend. If it succeeds but the page still fails:

- Check `STEM_SEPARATOR_ALLOWED_ORIGINS`.
- Allow local-network access in the browser.
- Check the browser developer console for CORS or Private Network Access
  messages.

### Unsupported file

Check the extension and 50 MB size cap. Container formats and extensions must
also be decodable by the installed FFmpeg build.

### Processing is slow

Open:

```text
http://127.0.0.1:8000/gpu-status
```

CPU-only Demucs processing can take several minutes.

### Stems do not load

Confirm that the backend is still running and that cleanup has not already been
requested. Output URLs are temporary loopback resources.

### Hosted site works locally but not after deployment

Add the exact deployed origin to `STEM_SEPARATOR_ALLOWED_ORIGINS`, restart the
backend, and allow the browser's local-network permission.
