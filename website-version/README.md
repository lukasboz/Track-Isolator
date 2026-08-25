# Stem Separator local web client

This Next.js application is a local-processing-only interface for
`../backend/app.py`.

It contains no authentication, billing, database, object storage, cloud job
system, or hosted inference worker. The browser sends audio directly to a
FastAPI service running on the same computer.

## Requirements

- Node.js 22
- npm
- The repository's Python backend
- FFmpeg and FFprobe

## Start locally

From the repository root, start the backend:

```bash
python backend/app.py --host 127.0.0.1 --port 8000
```

Then:

```bash
cd website-version
npm ci
npm run dev
```

Open:

```text
http://localhost:3000
```

## Commands

```bash
npm run dev
npm run lint
npm run typecheck
npm run build
npm run start
```

## Configuration

The optional `.env.local` setting is:

```text
NEXT_PUBLIC_LOCAL_BACKEND_URL=http://127.0.0.1:8000
```

The same value is the application default.

For a hosted UI, configure the local backend's allowed origin:

```text
STEM_SEPARATOR_ALLOWED_ORIGINS=https://your-site.example
```

## Documentation

- [Comprehensive web documentation](../docs/WEB_TECHNICAL.md)
- [Shared architecture](../ARCHITECTURE.md)
- [Vercel deployment](./VERCEL_FREE_DEPLOYMENT.md)
