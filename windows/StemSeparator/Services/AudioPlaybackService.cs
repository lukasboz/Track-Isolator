using System.Diagnostics;
using System.Windows.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using StemSeparator.Models;
using StemSeparator.Utilities;

namespace StemSeparator.Services;

public class AudioPlaybackService : IDisposable
{
    private readonly Dictionary<StemType, AudioFileReader> _readers = new();
    private readonly Dictionary<StemType, VolumeSampleProvider> _volumeProviders = new();
    private MixingSampleProvider? _mixer;
    private WaveOutEvent? _outputDevice;
    private DispatcherTimer? _positionTimer;
    private bool _disposed;

    public double Duration { get; private set; }

    public double CurrentTime
    {
        get
        {
            if (_readers.Count == 0) return 0;
            var first = _readers.Values.First();
            return first.CurrentTime.TotalSeconds;
        }
    }

    public bool IsPlaying =>
        _outputDevice?.PlaybackState == PlaybackState.Playing;

    public event Action<double>? PositionChanged;
    public event Action? PlaybackStopped;

    public void LoadStems(Dictionary<StemType, string> localPaths)
    {
        DisposePlaybackResources();

        if (localPaths.Count == 0)
            throw new ArgumentException("No stem paths provided.", nameof(localPaths));

        // Create AudioFileReader and VolumeSampleProvider for each stem
        WaveFormat? targetFormat = null;

        foreach (var (stemType, path) in localPaths)
        {
            var reader = new AudioFileReader(path);
            _readers[stemType] = reader;

            if (targetFormat is null)
                targetFormat = reader.WaveFormat;

            var volumeProvider = new VolumeSampleProvider(reader)
            {
                Volume = 1.0f
            };
            _volumeProviders[stemType] = volumeProvider;
        }

        // Build mixer from all volume providers
        _mixer = new MixingSampleProvider(
            _volumeProviders.Values.Cast<ISampleProvider>())
        {
            ReadFully = true
        };

        // Set duration from the longest stem
        Duration = _readers.Values
            .Max(r => r.TotalTime.TotalSeconds);

        // Create output device and init with mixer
        _outputDevice = new WaveOutEvent();
        _outputDevice.Init(_mixer);

        _outputDevice.PlaybackStopped += OnPlaybackStopped;

        // Start the position update timer
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Constants.PositionUpdateIntervalMs)
        };
        _positionTimer.Tick += (_, _) =>
        {
            if (IsPlaying)
                PositionChanged?.Invoke(CurrentTime);
        };
        _positionTimer.Start();

        Debug.WriteLine(
            $"[AudioPlaybackService] Loaded {localPaths.Count} stems, " +
            $"duration={Duration:F1}s");
    }

    public void Play()
    {
        _outputDevice?.Play();
    }

    public void Pause()
    {
        _outputDevice?.Pause();
    }

    public void Stop()
    {
        if (_outputDevice is null) return;

        _outputDevice.Stop();
        Seek(0);
    }

    public void Seek(double seconds)
    {
        var target = TimeSpan.FromSeconds(
            Math.Clamp(seconds, 0, Duration));

        foreach (var reader in _readers.Values)
        {
            reader.CurrentTime = target;
        }

        PositionChanged?.Invoke(seconds);
    }

    public void SetVolume(StemType stem, float volume)
    {
        if (_volumeProviders.TryGetValue(stem, out var provider))
        {
            provider.Volume = volume;
            Debug.WriteLine(
                $"[AudioPlaybackService] {stem} volume set to {volume:F2}");
        }
    }

    /// <summary>
    /// Mixes only the unmuted stems into a single WAV file.
    /// </summary>
    public void ExportMix(string outputPath, Dictionary<StemType, bool> mutedStems)
    {
        // Create fresh readers for export (don't disturb playback readers)
        var exportReaders = new List<AudioFileReader>();
        var exportProviders = new List<ISampleProvider>();

        try
        {
            foreach (var (stem, reader) in _readers)
            {
                var isMuted = mutedStems.TryGetValue(stem, out var m) && m;
                if (isMuted) continue;

                // Create a new reader from the same file
                var exportReader = new AudioFileReader(reader.FileName);
                exportReaders.Add(exportReader);
                exportProviders.Add(exportReader);
            }

            if (exportProviders.Count == 0)
                throw new InvalidOperationException("All stems are muted. Unmute at least one stem to export.");

            var mixer = new MixingSampleProvider(exportProviders) { ReadFully = false };
            WaveFileWriter.CreateWaveFile16(outputPath, mixer);

            Debug.WriteLine($"[AudioPlaybackService] Exported mix to {outputPath}");
        }
        finally
        {
            foreach (var r in exportReaders)
                r.Dispose();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Debug.WriteLine(
                $"[AudioPlaybackService] Playback error: {e.Exception.Message}");
        }

        // Fire final position update
        PositionChanged?.Invoke(CurrentTime);
        PlaybackStopped?.Invoke();
    }

    private void DisposePlaybackResources()
    {
        _positionTimer?.Stop();
        _positionTimer = null;

        if (_outputDevice is not null)
        {
            _outputDevice.PlaybackStopped -= OnPlaybackStopped;
            _outputDevice.Stop();
            _outputDevice.Dispose();
            _outputDevice = null;
        }

        _mixer = null;

        _volumeProviders.Clear();

        foreach (var reader in _readers.Values)
        {
            reader.Dispose();
        }
        _readers.Clear();

        Duration = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposePlaybackResources();
        GC.SuppressFinalize(this);
    }
}
