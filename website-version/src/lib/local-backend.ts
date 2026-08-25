export const STEM_NAMES = ["vocals", "drums", "bass", "other"] as const;
export type StemName = (typeof STEM_NAMES)[number];
export type StemUrls = Record<StemName, string>;

export interface BackendHealth {
  status: string;
  device: string;
  gpu_name?: string | null;
  model_loaded?: boolean;
}

export interface SeparationProgress {
  status?: string;
  progress?: number;
}

interface SeparationEvent extends SeparationProgress {
  stems?: Record<string, string>;
  error?: string;
  elapsed_seconds?: number;
}

export interface SeparationResult {
  requestId: string;
  stemUrls: StemUrls;
  elapsedSeconds?: number;
}

export const MAX_FILE_BYTES = 50 * 1024 * 1024;
export const SUPPORTED_EXTENSIONS = [
  ".mp3",
  ".wav",
  ".flac",
  ".ogg",
  ".m4a",
  ".aac",
  ".aif",
  ".aiff",
] as const;

const DEFAULT_BACKEND_URL = "http://127.0.0.1:8000";

export function getBackendUrl(): string {
  return (
    process.env.NEXT_PUBLIC_LOCAL_BACKEND_URL || DEFAULT_BACKEND_URL
  ).replace(/\/+$/, "");
}

export function validateAudioFile(file: File): string | null {
  const dot = file.name.lastIndexOf(".");
  const extension = dot >= 0 ? file.name.slice(dot).toLowerCase() : "";

  if (!SUPPORTED_EXTENSIONS.includes(
    extension as (typeof SUPPORTED_EXTENSIONS)[number],
  )) {
    return `Unsupported file type. Choose ${SUPPORTED_EXTENSIONS.join(", ")}.`;
  }

  if (file.size <= 0) {
    return "The selected file is empty.";
  }

  if (file.size > MAX_FILE_BYTES) {
    return `The selected file is ${(file.size / 1024 / 1024).toFixed(1)} MB. The limit is 50 MB.`;
  }

  return null;
}

export async function getBackendHealth(
  backendUrl: string,
  signal?: AbortSignal,
): Promise<BackendHealth> {
  const response = await fetch(`${backendUrl}/health`, {
    cache: "no-store",
    signal,
  });

  if (!response.ok) {
    throw new Error(`Local backend health check returned ${response.status}.`);
  }

  const health = (await response.json()) as BackendHealth;
  if (health.status !== "healthy") {
    throw new Error("The local backend did not report a healthy state.");
  }
  return health;
}

export async function cleanupRequest(
  backendUrl: string,
  requestId: string | null,
  keepalive = false,
): Promise<void> {
  if (!requestId) return;

  await fetch(`${backendUrl}/cleanup/${encodeURIComponent(requestId)}`, {
    method: "DELETE",
    keepalive,
  }).catch(() => undefined);
}

export async function separateAudioLocally({
  backendUrl,
  file,
  preview,
  signal,
  onProgress,
}: {
  backendUrl: string;
  file: File;
  preview: boolean;
  signal: AbortSignal;
  onProgress: (progress: SeparationProgress) => void;
}): Promise<SeparationResult> {
  const validationError = validateAudioFile(file);
  if (validationError) throw new Error(validationError);

  const formData = new FormData();
  formData.append("audio", file);

  const query = new URLSearchParams({
    model: "htdemucs",
    preview: String(preview),
  });
  const response = await fetch(
    `${backendUrl}/separate-stream?${query.toString()}`,
    {
      method: "POST",
      body: formData,
      signal,
    },
  );

  if (!response.ok || !response.body) {
    throw new Error(await responseError(response));
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let result: SeparationResult | null = null;

  const processBlock = (block: string) => {
    const data = block
      .split("\n")
      .filter((line) => line.startsWith("data:"))
      .map((line) => line.slice(5).trimStart())
      .join("\n");
    if (!data) return;

    let event: SeparationEvent;
    try {
      event = JSON.parse(data) as SeparationEvent;
    } catch {
      throw new Error("The local backend returned an invalid progress event.");
    }

    if (event.error) throw new Error(event.error);
    onProgress({ status: event.status, progress: event.progress });

    if (event.stems) {
      const stemUrls = completeStemMap(event.stems, backendUrl);
      result = {
        requestId: requestIdFromStems(event.stems),
        stemUrls,
        elapsedSeconds: event.elapsed_seconds,
      };
    }
  };

  while (true) {
    const { done, value } = await reader.read();
    buffer += decoder.decode(value, { stream: !done });
    buffer = buffer.replace(/\r\n/g, "\n");

    let boundary = buffer.indexOf("\n\n");
    while (boundary >= 0) {
      processBlock(buffer.slice(0, boundary));
      buffer = buffer.slice(boundary + 2);
      boundary = buffer.indexOf("\n\n");
    }

    if (done) break;
  }

  if (buffer.trim()) processBlock(buffer);
  if (!result) {
    throw new Error("Separation ended without returning the four expected stems.");
  }
  return result;
}

function completeStemMap(
  stems: Record<string, string>,
  backendUrl: string,
): StemUrls {
  for (const name of STEM_NAMES) {
    if (typeof stems[name] !== "string") {
      throw new Error(`The local backend did not return the ${name} stem.`);
    }
  }

  return {
    vocals: new URL(stems.vocals, backendUrl).toString(),
    drums: new URL(stems.drums, backendUrl).toString(),
    bass: new URL(stems.bass, backendUrl).toString(),
    other: new URL(stems.other, backendUrl).toString(),
  };
}

function requestIdFromStems(stems: Record<string, string>): string {
  const firstPath = Object.values(stems)[0];
  const match = firstPath?.match(/^\/outputs\/([^/]+)\//);
  if (!match) {
    throw new Error("The local backend returned an invalid output path.");
  }
  return match[1];
}

async function responseError(response: Response): Promise<string> {
  const text = await response.text().catch(() => "");
  try {
    const body = JSON.parse(text) as { detail?: unknown; error?: unknown };
    const message = body.detail ?? body.error;
    if (typeof message === "string" && message.trim()) return message;
  } catch {
    if (text.trim()) return text;
  }
  return `The local backend returned ${response.status}.`;
}
