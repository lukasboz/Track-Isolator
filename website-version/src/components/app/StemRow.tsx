"use client";

interface StemRowProps {
  name: string;
  muted: boolean;
  onToggleMute: () => void;
  downloadUrl: string;
  color: string;
}

export default function StemRow({
  name,
  muted,
  onToggleMute,
  downloadUrl,
  color,
}: StemRowProps) {
  return (
    <div
      className={`flex items-center justify-between py-3 pl-4 pr-3 rounded-md transition-opacity border-l-2 ${
        muted ? "opacity-40" : ""
      }`}
      style={{ borderLeftColor: color }}
    >
      <div className="flex items-center gap-4 min-w-0">
        <button
          onClick={onToggleMute}
          aria-label={muted ? `Unmute ${name}` : `Mute ${name}`}
          className="font-mono text-[10px] uppercase tracking-wideish text-ink-200 hover:text-ink-50 transition-colors w-10 text-left"
        >
          {muted ? "off" : "on"}
        </button>
        <span className="text-sm text-ink-50 truncate">{name}</span>
      </div>

      <a
        href={downloadUrl}
        download
        className="font-mono text-[10px] uppercase tracking-wideish text-ink-300 hover:text-coral-500 transition-colors"
        onClick={(e) => e.stopPropagation()}
      >
        ↓ download
      </a>
    </div>
  );
}
