"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import StemPlayer from "@/components/app/StemPlayer";
import {
  cleanupRequest,
  getBackendHealth,
  getBackendUrl,
  separateAudioLocally,
  validateAudioFile,
  type BackendHealth,
  type StemUrls,
} from "@/lib/local-backend";

export default function LocalSeparator() {
  const backendUrl = useMemo(() => getBackendUrl(), []);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const activeControllerRef = useRef<AbortController | null>(null);
  const requestIdRef = useRef<string | null>(null);

  const [health, setHealth] = useState<BackendHealth | null>(null);
  const [checking, setChecking] = useState(true);
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState(false);
  const [processing, setProcessing] = useState(false);
  const [progress, setProgress] = useState(0);
  const [statusText, setStatusText] = useState("Waiting for a track.");
  const [error, setError] = useState("");
  const [stemUrls, setStemUrls] = useState<StemUrls | null>(null);

  const replaceRequestId = useCallback((next: string | null) => {
    requestIdRef.current = next;
  }, []);

  const checkBackend = useCallback(async () => {
    setChecking(true);
    try {
      const nextHealth = await getBackendHealth(backendUrl);
      setHealth(nextHealth);
      setError("");
    } catch {
      setHealth(null);
    } finally {
      setChecking(false);
    }
  }, [backendUrl]);

  useEffect(() => {
    const controller = new AbortController();
    void getBackendHealth(backendUrl, controller.signal)
      .then((nextHealth) => {
        setHealth(nextHealth);
        setError("");
      })
      .catch(() => setHealth(null))
      .finally(() => setChecking(false));
    return () => controller.abort();
  }, [backendUrl]);

  useEffect(() => {
    return () => {
      activeControllerRef.current?.abort();
      void cleanupRequest(backendUrl, requestIdRef.current, true);
    };
  }, [backendUrl]);

  const reset = useCallback(async () => {
    activeControllerRef.current?.abort();
    await cleanupRequest(backendUrl, requestIdRef.current);
    replaceRequestId(null);
    setFile(null);
    setStemUrls(null);
    setProgress(0);
    setStatusText("Waiting for a track.");
    setError("");
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, [backendUrl, replaceRequestId]);

  const separate = async () => {
    if (!file || processing) return;

    const validationError = validateAudioFile(file);
    if (validationError) {
      setError(validationError);
      return;
    }
    if (!health) {
      setError("Start the local backend, then click Check engine.");
      return;
    }

    await cleanupRequest(backendUrl, requestIdRef.current);
    replaceRequestId(null);

    const controller = new AbortController();
    activeControllerRef.current = controller;
    setProcessing(true);
    setStemUrls(null);
    setProgress(0);
    setError("");
    setStatusText("Uploading to the local engine...");

    try {
      const result = await separateAudioLocally({
        backendUrl,
        file,
        preview,
        signal: controller.signal,
        onProgress: (event) => {
          if (event.status) setStatusText(event.status);
          if (typeof event.progress === "number") {
            setProgress(event.progress);
          }
        },
      });

      replaceRequestId(result.requestId);
      setStemUrls(result.stemUrls);
      setProgress(100);
      setStatusText(
        `Complete${result.elapsedSeconds ? ` in ${result.elapsedSeconds}s` : ""}.`,
      );
    } catch (caught) {
      if (caught instanceof DOMException && caught.name === "AbortError") {
        setStatusText("Separation canceled.");
      } else {
        setError(
          caught instanceof Error ? caught.message : "Local separation failed.",
        );
        setStatusText("Separation stopped.");
      }
    } finally {
      if (activeControllerRef.current === controller) {
        activeControllerRef.current = null;
      }
      setProcessing(false);
    }
  };

  return (
    <main className="min-h-screen px-5 py-10 sm:px-8">
      <div className="mx-auto max-w-5xl">
        <header className="flex flex-col gap-5 border-b border-ink-50/[0.08] pb-8 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <p className="font-mono text-xs uppercase tracking-wideish text-coral-500">
              local processing only
            </p>
            <h1 className="display mt-3 text-4xl text-ink-50 sm:text-6xl">
              Stem<span className="text-coral-500">·</span>Sep
            </h1>
            <p className="mt-3 max-w-2xl text-ink-200">
              Separate vocals, drums, bass, and other instruments without
              uploading your music to a cloud service.
            </p>
          </div>

          <div className="rounded-md border border-ink-50/[0.08] px-4 py-3 font-mono text-xs">
            <div className="flex items-center gap-2">
              <span
                className={`h-2 w-2 rounded-full ${
                  health ? "bg-olive" : "bg-coral-500"
                }`}
              />
              <span className={health ? "text-olive" : "text-ink-200"}>
                {checking
                  ? "checking local engine"
                  : health
                    ? `${health.device}${health.gpu_name ? ` · ${health.gpu_name}` : ""}`
                    : "local engine offline"}
              </span>
            </div>
            <p className="mt-1 text-ink-300">{backendUrl}</p>
          </div>
        </header>

        {!health && !checking && (
          <section className="mt-8 rounded-lg border border-sand/30 bg-sand/[0.04] p-5">
            <h2 className="display text-2xl text-ink-50">
              Start the local engine
            </h2>
            <p className="mt-2 text-sm leading-relaxed text-ink-200">
              From the repository root, activate the backend environment and
              run:
            </p>
            <pre className="mt-4 overflow-x-auto rounded-md bg-ink-900 p-4 text-xs text-ink-100">
              <code>python backend/app.py --host 127.0.0.1 --port 8000</code>
            </pre>
            <button
              type="button"
              onClick={() => void checkBackend()}
              className="mt-4 rounded-md bg-coral-500 px-4 py-2 text-sm font-medium text-ink-900 hover:bg-coral-600"
            >
              Check engine
            </button>
          </section>
        )}

        <section className="mt-10 rounded-lg border border-ink-50/[0.08] bg-ink-800/30 p-6 sm:p-8">
          <div className="flex flex-col gap-6 sm:flex-row sm:items-end sm:justify-between">
            <div className="flex-1">
              <label className="font-mono text-xs uppercase tracking-wideish text-ink-300">
                Audio file
              </label>
              <input
                ref={fileInputRef}
                type="file"
                accept=".mp3,.wav,.flac,.ogg,.m4a,.aac,.aif,.aiff"
                disabled={processing}
                onChange={(event) => {
                  const nextFile = event.target.files?.[0] ?? null;
                  setFile(nextFile);
                  setError(nextFile ? validateAudioFile(nextFile) ?? "" : "");
                }}
                className="mt-3 block w-full text-sm text-ink-100 file:mr-4 file:rounded-md file:border-0 file:bg-coral-500 file:px-4 file:py-2 file:font-medium file:text-ink-900 hover:file:bg-coral-600"
              />
              <label className="mt-4 flex items-center gap-2 text-sm text-ink-200">
                <input
                  type="checkbox"
                  checked={preview}
                  disabled={processing}
                  onChange={(event) => setPreview(event.target.checked)}
                  className="accent-coral-500"
                />
                Process only the first 30 seconds
              </label>
              <p className="mt-3 text-xs text-ink-300">
                Maximum file size: 50 MB. Processing and output files stay on
                this computer.
              </p>
            </div>

            {processing ? (
              <button
                type="button"
                onClick={() => activeControllerRef.current?.abort()}
                className="rounded-md border border-coral-500/50 px-5 py-3 font-medium text-coral-500 transition-colors hover:bg-coral-500/10"
              >
                Cancel
              </button>
            ) : (
              <button
                type="button"
                onClick={() => void separate()}
                disabled={!file || !health || Boolean(error)}
                className="rounded-md bg-coral-500 px-5 py-3 font-medium text-ink-900 transition-colors hover:bg-coral-600 disabled:cursor-not-allowed disabled:opacity-40"
              >
                Separate locally
              </button>
            )}
          </div>

          <div className="mt-7">
            <div className="mb-2 flex justify-between text-xs text-ink-300">
              <span>{statusText}</span>
              <span className="font-mono">{progress}%</span>
            </div>
            <div className="h-1 overflow-hidden rounded-full bg-ink-50/[0.06]">
              <div
                className="h-full bg-coral-500 transition-[width] duration-300"
                style={{ width: `${progress}%` }}
              />
            </div>
          </div>

          {error && (
            <p className="mt-5 border-l-2 border-coral-500 pl-4 text-sm text-coral-500">
              {error}
            </p>
          )}
        </section>

        {stemUrls && file && (
          <section className="mt-10">
            <StemPlayer stemUrls={stemUrls} songName={file.name} />
            <button
              type="button"
              onClick={() => void reset()}
              className="mt-5 text-sm text-ink-200 underline decoration-ink-400/60 underline-offset-4 hover:text-coral-500"
            >
              Separate another track
            </button>
          </section>
        )}

        <footer className="mt-16 border-t border-ink-50/[0.06] pt-6 text-xs text-ink-300">
          No accounts, billing, database, object storage, or cloud inference.
        </footer>
      </div>
    </main>
  );
}
