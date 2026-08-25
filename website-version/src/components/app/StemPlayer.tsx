"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import DownloadMix from "./DownloadMix";
import StemRow from "./StemRow";
import {
  STEM_NAMES,
  type StemName,
  type StemUrls,
} from "@/lib/local-backend";

interface StemPlayerProps {
  stemUrls: StemUrls;
  songName: string;
}

type AudioBufferMap = Record<StemName, AudioBuffer>;
type GainNodeMap = Partial<Record<StemName, GainNode>>;
type SourceNodeMap = Partial<Record<StemName, AudioBufferSourceNode>>;

const STEM_LABELS: Record<StemName, string> = {
  vocals: "Vocals",
  drums: "Drums",
  bass: "Bass",
  other: "Other",
};

export const STEM_COLORS: Record<StemName, string> = {
  vocals: "#ff5b3a",
  drums: "#d9b97a",
  bass: "#7d8a99",
  other: "#9ba66a",
};

export default function StemPlayer({ stemUrls, songName }: StemPlayerProps) {
  const contextRef = useRef<AudioContext | null>(null);
  const buffersRef = useRef<AudioBufferMap | null>(null);
  const gainsRef = useRef<GainNodeMap>({});
  const sourcesRef = useRef<SourceNodeMap>({});
  const startOffsetRef = useRef(0);
  const startedAtRef = useRef(0);
  const durationRef = useRef(0);
  const isPlayingRef = useRef(false);
  const playbackGenerationRef = useRef(0);
  const mutedRef = useRef<Record<StemName, boolean>>({
    vocals: false,
    drums: false,
    bass: false,
    other: false,
  });

  const [loaded, setLoaded] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration] = useState(0);
  const [muted, setMuted] = useState<Record<StemName, boolean>>({
    vocals: false,
    drums: false,
    bass: false,
    other: false,
  });

  const stopSources = useCallback(() => {
    playbackGenerationRef.current += 1;
    for (const source of Object.values(sourcesRef.current)) {
      if (!source) continue;
      source.onended = null;
      try {
        source.stop();
      } catch {
        // A source may already have ended.
      }
      source.disconnect();
    }
    sourcesRef.current = {};
  }, []);

  const playbackPosition = useCallback(() => {
    const context = contextRef.current;
    if (!context || !isPlayingRef.current) return startOffsetRef.current;

    const elapsed = Math.max(0, context.currentTime - startedAtRef.current);
    return Math.min(durationRef.current, startOffsetRef.current + elapsed);
  }, []);

  const startPlayback = useCallback(
    async (offset: number) => {
      const context = contextRef.current;
      const buffers = buffersRef.current;
      if (!context || !buffers) return;

      await context.resume();
      stopSources();

      const normalizedOffset =
        offset >= durationRef.current ? 0 : Math.max(0, offset);
      const startAt = context.currentTime + 0.025;
      const generation = playbackGenerationRef.current;
      const nextSources: SourceNodeMap = {};

      for (const name of STEM_NAMES) {
        const source = context.createBufferSource();
        source.buffer = buffers[name];
        source.connect(gainsRef.current[name]!);
        source.start(startAt, normalizedOffset);
        nextSources[name] = source;
      }

      nextSources.vocals!.onended = () => {
        if (
          playbackGenerationRef.current !== generation ||
          !isPlayingRef.current
        ) {
          return;
        }
        isPlayingRef.current = false;
        startOffsetRef.current = 0;
        setCurrentTime(0);
        setIsPlaying(false);
        sourcesRef.current = {};
      };

      sourcesRef.current = nextSources;
      startOffsetRef.current = normalizedOffset;
      startedAtRef.current = startAt;
      isPlayingRef.current = true;
      setCurrentTime(normalizedOffset);
      setIsPlaying(true);
    },
    [stopSources],
  );

  const pausePlayback = useCallback(() => {
    const nextPosition = playbackPosition();
    isPlayingRef.current = false;
    startOffsetRef.current = nextPosition;
    setCurrentTime(nextPosition);
    setIsPlaying(false);
    stopSources();
  }, [playbackPosition, stopSources]);

  useEffect(() => {
    const controller = new AbortController();
    let disposed = false;

    const load = async () => {
      setLoaded(false);
      setLoadError("");
      setCurrentTime(0);
      setDuration(0);

      const context = new AudioContext();
      contextRef.current = context;

      try {
        const entries = await Promise.all(
          STEM_NAMES.map(async (name) => {
            const response = await fetch(stemUrls[name], {
              cache: "no-store",
              signal: controller.signal,
            });
            if (!response.ok) {
              throw new Error(`Could not load the ${name} stem.`);
            }
            const encoded = await response.arrayBuffer();
            const decoded = await context.decodeAudioData(encoded);
            return [name, decoded] as const;
          }),
        );
        if (disposed) return;

        const buffers = Object.fromEntries(entries) as AudioBufferMap;
        const nextGains: GainNodeMap = {};
        for (const name of STEM_NAMES) {
          const gain = context.createGain();
          gain.gain.value = mutedRef.current[name] ? 0 : 1;
          gain.connect(context.destination);
          nextGains[name] = gain;
        }

        const nextDuration = Math.max(
          ...STEM_NAMES.map((name) => buffers[name].duration),
        );
        buffersRef.current = buffers;
        gainsRef.current = nextGains;
        durationRef.current = nextDuration;
        startOffsetRef.current = 0;
        setDuration(nextDuration);
        setLoaded(true);
      } catch (error) {
        if (
          !disposed &&
          !(error instanceof DOMException && error.name === "AbortError")
        ) {
          setLoadError(
            error instanceof Error ? error.message : "Could not load stems.",
          );
        }
      }
    };

    void load();
    return () => {
      disposed = true;
      controller.abort();
      isPlayingRef.current = false;
      stopSources();
      for (const gain of Object.values(gainsRef.current)) {
        gain?.disconnect();
      }
      gainsRef.current = {};
      buffersRef.current = null;
      void contextRef.current?.close();
      contextRef.current = null;
    };
  }, [stemUrls, stopSources]);

  useEffect(() => {
    mutedRef.current = muted;
    const context = contextRef.current;
    if (!context) return;

    for (const name of STEM_NAMES) {
      const gain = gainsRef.current[name];
      if (gain) {
        gain.gain.setTargetAtTime(muted[name] ? 0 : 1, context.currentTime, 0.01);
      }
    }
  }, [muted]);

  useEffect(() => {
    if (!isPlaying) return;

    let frame = 0;
    const update = () => {
      setCurrentTime(playbackPosition());
      frame = window.requestAnimationFrame(update);
    };
    frame = window.requestAnimationFrame(update);
    return () => window.cancelAnimationFrame(frame);
  }, [isPlaying, playbackPosition]);

  const togglePlay = () => {
    if (isPlayingRef.current) {
      pausePlayback();
    } else {
      void startPlayback(startOffsetRef.current);
    }
  };

  const handleSeek = (event: React.ChangeEvent<HTMLInputElement>) => {
    const nextTime = Number.parseFloat(event.target.value);
    if (!Number.isFinite(nextTime)) return;

    const wasPlaying = isPlayingRef.current;
    isPlayingRef.current = false;
    stopSources();
    startOffsetRef.current = nextTime;
    setCurrentTime(nextTime);
    setIsPlaying(false);
    if (wasPlaying) void startPlayback(nextTime);
  };

  const toggleMute = (name: StemName) => {
    setMuted((previous) => ({ ...previous, [name]: !previous[name] }));
  };

  const unmutedStems = STEM_NAMES.filter((name) => !muted[name]);
  const seekPercent = duration ? (currentTime / duration) * 100 : 0;

  return (
    <div className="rounded-lg border border-ink-50/[0.06] bg-ink-800/40 p-5 sm:p-7">
      {loadError && (
        <p className="mb-5 border-l-2 border-coral-500 pl-4 text-sm text-coral-500">
          {loadError}
        </p>
      )}

      <div className="mb-7 flex items-center gap-4">
        <button
          type="button"
          onClick={togglePlay}
          disabled={!loaded}
          aria-label={isPlaying ? "Pause" : "Play"}
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-coral-500 text-ink-900 transition-colors hover:bg-coral-600 disabled:bg-ink-700 disabled:text-ink-300"
        >
          {isPlaying ? (
            <svg className="h-4 w-4" fill="currentColor" viewBox="0 0 24 24">
              <path d="M6 4h4v16H6zM14 4h4v16h-4z" />
            </svg>
          ) : (
            <svg className="ml-0.5 h-4 w-4" fill="currentColor" viewBox="0 0 24 24">
              <path d="M8 5v14l11-7z" />
            </svg>
          )}
        </button>

        <div className="relative flex-1">
          <input
            type="range"
            min={0}
            max={duration || 0}
            step={0.05}
            value={currentTime}
            onChange={handleSeek}
            disabled={!loaded}
            aria-label="Playback position"
            className="w-full cursor-pointer appearance-none bg-transparent
              [&::-webkit-slider-runnable-track]:h-[3px]
              [&::-webkit-slider-runnable-track]:rounded-full
              [&::-webkit-slider-runnable-track]:bg-ink-50/10
              [&::-webkit-slider-thumb]:-mt-[5px]
              [&::-webkit-slider-thumb]:h-3
              [&::-webkit-slider-thumb]:w-3
              [&::-webkit-slider-thumb]:appearance-none
              [&::-webkit-slider-thumb]:rounded-full
              [&::-webkit-slider-thumb]:bg-coral-500
              [&::-moz-range-track]:h-[3px]
              [&::-moz-range-track]:rounded-full
              [&::-moz-range-track]:bg-ink-50/10
              [&::-moz-range-thumb]:h-3
              [&::-moz-range-thumb]:w-3
              [&::-moz-range-thumb]:rounded-full
              [&::-moz-range-thumb]:border-0
              [&::-moz-range-thumb]:bg-coral-500"
            style={{
              background: `linear-gradient(to right, #ff5b3a 0%, #ff5b3a ${seekPercent}%, transparent ${seekPercent}%, transparent 100%)`,
              backgroundClip: "content-box",
            }}
          />
        </div>

        <span className="shrink-0 font-mono text-xs tabular-nums text-ink-300">
          {formatTime(currentTime)} / {formatTime(duration)}
        </span>
      </div>

      <div className="mb-6 space-y-2">
        {STEM_NAMES.map((name) => (
          <StemRow
            key={name}
            name={STEM_LABELS[name]}
            muted={muted[name]}
            onToggleMute={() => toggleMute(name)}
            downloadUrl={stemUrls[name]}
            color={STEM_COLORS[name]}
          />
        ))}
      </div>

      <div className="flex items-center justify-between border-t border-ink-50/[0.06] pt-5">
        <p className="font-mono text-xs text-ink-300">
          {unmutedStems.length} of 4 stems live
        </p>
        <DownloadMix
          stemUrls={stemUrls}
          unmutedStems={unmutedStems}
          songName={songName}
        />
      </div>
    </div>
  );
}

function formatTime(seconds: number): string {
  if (!Number.isFinite(seconds)) return "0:00";
  const minutes = Math.floor(seconds / 60);
  const remainder = Math.floor(seconds % 60);
  return `${minutes}:${remainder.toString().padStart(2, "0")}`;
}
