"use client";

import { useState } from "react";
import type { StemName, StemUrls } from "@/lib/local-backend";

interface DownloadMixProps {
  stemUrls: StemUrls;
  unmutedStems: StemName[];
  songName: string;
}

export default function DownloadMix({
  stemUrls,
  unmutedStems,
  songName,
}: DownloadMixProps) {
  const [downloading, setDownloading] = useState(false);

  const handleDownloadMix = async () => {
    if (unmutedStems.length === 0) return;
    setDownloading(true);
    let url: string | null = null;
    let audioContext: AudioContext | null = null;
    try {
      const AudioCtx =
        window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      audioContext = new AudioCtx();
      const context = audioContext;
      const buffers = await Promise.all(
        unmutedStems.map(async (stem) => {
          const response = await fetch(stemUrls[stem]);
          if (!response.ok) {
            throw new Error(`Failed to download ${stem} (${response.status}).`);
          }
          const arrayBuffer = await response.arrayBuffer();
          return context.decodeAudioData(arrayBuffer);
        }),
      );

      if (buffers.length === 0) return;

      const maxDuration = Math.max(...buffers.map((b) => b.duration));
      const sampleRate = buffers[0].sampleRate;
      const numChannels = Math.max(...buffers.map((b) => b.numberOfChannels));
      const totalSamples = Math.ceil(maxDuration * sampleRate);

      const offlineCtx = new OfflineAudioContext(numChannels, totalSamples, sampleRate);
      for (const buffer of buffers) {
        const source = offlineCtx.createBufferSource();
        source.buffer = buffer;
        source.connect(offlineCtx.destination);
        source.start(0);
      }

      const renderedBuffer = await offlineCtx.startRendering();
      const wavBlob = audioBufferToWav(renderedBuffer);

      const baseName = songName.replace(/\.[^/.]+$/, "");
      url = URL.createObjectURL(wavBlob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${baseName}_mix.wav`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
    } catch (err) {
      console.error("Failed to build mix:", err);
      alert("Couldn't build that mix. Try again in a moment.");
    } finally {
      if (url) URL.revokeObjectURL(url);
      if (audioContext) audioContext.close().catch(() => undefined);
      setDownloading(false);
    }
  };

  const disabled = downloading || unmutedStems.length === 0;

  return (
    <button
      onClick={handleDownloadMix}
      disabled={disabled}
      className="inline-flex items-center gap-2 px-3 py-1.5 text-xs font-medium text-ink-50 bg-coral-500/[0.08] hover:bg-coral-500/[0.16] border border-coral-500/30 rounded-md transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
    >
      {downloading ? (
        <>
          <span className="w-3 h-3 border border-coral-500/30 border-t-coral-500 rounded-full animate-spin" />
          <span>Mixing…</span>
        </>
      ) : (
        <>
          <span aria-hidden>↓</span>
          <span>Download mix</span>
        </>
      )}
    </button>
  );
}

function audioBufferToWav(buffer: AudioBuffer): Blob {
  const numChannels = buffer.numberOfChannels;
  const sampleRate = buffer.sampleRate;
  const format = 1; // PCM
  const bitsPerSample = 16;
  const bytesPerSample = bitsPerSample / 8;
  const blockAlign = numChannels * bytesPerSample;
  const dataLength = buffer.length * blockAlign;
  const headerLength = 44;
  const totalLength = headerLength + dataLength;

  const arrayBuffer = new ArrayBuffer(totalLength);
  const view = new DataView(arrayBuffer);

  writeString(view, 0, "RIFF");
  view.setUint32(4, totalLength - 8, true);
  writeString(view, 8, "WAVE");
  writeString(view, 12, "fmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, format, true);
  view.setUint16(22, numChannels, true);
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * blockAlign, true);
  view.setUint16(32, blockAlign, true);
  view.setUint16(34, bitsPerSample, true);
  writeString(view, 36, "data");
  view.setUint32(40, dataLength, true);

  const channels: Float32Array[] = [];
  for (let ch = 0; ch < numChannels; ch++) {
    channels.push(buffer.getChannelData(ch));
  }

  let offset = 44;
  for (let i = 0; i < buffer.length; i++) {
    for (let ch = 0; ch < numChannels; ch++) {
      const sample = Math.max(-1, Math.min(1, channels[ch][i]));
      const intSample = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
      view.setInt16(offset, intSample, true);
      offset += 2;
    }
  }

  return new Blob([arrayBuffer], { type: "audio/wav" });
}

function writeString(view: DataView, offset: number, str: string) {
  for (let i = 0; i < str.length; i++) {
    view.setUint8(offset + i, str.charCodeAt(i));
  }
}
