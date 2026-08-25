# Deploy the local-only interface to Vercel

This guide describes the repository as of August 25, 2026.

Vercel hosts only the browser interface. Audio processing remains on the
computer opening the site.

```text
Vercel-hosted HTML, CSS, and JavaScript
                  │
                  │ browser request to loopback
                  ▼
        http://127.0.0.1:8000
                  │
                  ▼
        local FastAPI and Demucs
```

The deployment does not use:

- A database
- Object storage
- Authentication
- Stripe
- Modal
- A cloud GPU
- Hosted audio uploads

## Requirements

- A Vercel account
- A Git repository containing `website-version/`
- Python, backend dependencies, FFmpeg, and FFprobe on every computer that
  will process audio

## 1. Verify locally

Start the backend:

```bash
python backend/app.py --host 127.0.0.1 --port 8000
```

Start the website:

```bash
cd website-version
npm ci
npm run dev
```

Open `http://localhost:3000` and complete a test separation.

## 2. Commit the website

`website-version/` contains its own Git repository in the current workspace.

If that repository is pushed directly, use `/` as the Vercel root directory.
If the parent repository is pushed, configure `website-version` as the Vercel
root directory.

Do not commit `.env.local`.

## 3. Import into Vercel

1. Create a Vercel project from the Git repository.
2. Select Next.js as the framework.
3. Use Node.js 22.
4. Do not configure database, storage, authentication, billing, or worker
   variables.
5. Deploy.

The frontend does not require a secret.

Optional:

```text
NEXT_PUBLIC_LOCAL_BACKEND_URL=http://127.0.0.1:8000
```

This is already the default.

## 4. Allow the deployed origin

The backend permits local development origins by default. A hosted site must be
added explicitly.

Windows PowerShell:

```powershell
$env:STEM_SEPARATOR_ALLOWED_ORIGINS="https://your-project.vercel.app"
python backend\app.py --host 127.0.0.1 --port 8000
```

macOS or Linux:

```bash
export STEM_SEPARATOR_ALLOWED_ORIGINS="https://your-project.vercel.app"
python backend/app.py --host 127.0.0.1 --port 8000
```

For a production domain and a preview domain:

```text
STEM_SEPARATOR_ALLOWED_ORIGINS=https://audio.example.com,https://your-project.vercel.app
```

Restart the backend after changing the variable.

## 5. Use the site

1. Start the backend on the same computer as the browser.
2. Open the Vercel site.
3. Allow local-network access if the browser prompts.
4. Confirm the status badge reports the local processing device.
5. Select a file.
6. Click Separate locally.

The browser posts the file directly to loopback. The request does not pass
through Vercel application code.

## Privacy boundary

Vercel receives normal static-site requests such as HTML, JavaScript, CSS, and
font requests.

Vercel does not receive:

- The selected audio file
- Demucs input tensors
- Generated stems
- Mix exports
- A processing job record

Generated files remain in the local backend's temporary directory until the
browser requests cleanup or the backend exits.

## Limitations

- The backend must be running whenever the site is used.
- Processing works only on the computer running that backend.
- `127.0.0.1` always refers to the device running the browser.
- A phone cannot use a backend running on a laptop through this configuration.
- Some managed browser policies block public sites from contacting loopback.
- The browser may require Private Network Access permission.
- CPU inference can take several minutes.

## Troubleshooting

### Local engine offline

Open:

```text
http://127.0.0.1:8000/health
```

If the URL fails, start the backend.

If it works but the hosted page still reports offline:

- Verify `STEM_SEPARATOR_ALLOWED_ORIGINS`.
- Match the origin exactly, including `https://`.
- Restart the backend.
- Allow local-network access.
- Check the browser console for CORS or Private Network Access errors.

### CORS error

Do not use `*` for a public deployment. Add the exact deployed origin to the
comma-separated backend allowlist.

### Mixed-content or local-network policy error

Modern browsers generally treat loopback as a potentially trustworthy local
origin, but enterprise policies can override that behavior. Use the application
locally if the managed browser blocks public-to-loopback access.

### Slow processing

Open:

```text
http://127.0.0.1:8000/gpu-status
```

The result shows whether CUDA, MPS, or CPU is active.

## Verification before deployment

Run:

```bash
npm run lint
npm run typecheck
npm run build
npm audit
```

The build should contain only static application routes and no `/api` routes.
