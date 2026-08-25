using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StemSeparator.Models;
using StemSeparator.Services;
using StemSeparator.Utilities;
using System.Collections.ObjectModel;

namespace StemSeparator.ViewModels;

public partial class StemPlayerViewModel : ObservableObject, IDisposable
{
    private readonly AudioPlaybackService _audioService;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private double _currentTime;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private string _currentTimeFormatted = "0:00";

    [ObservableProperty]
    private string _durationFormatted = "0:00";

    public ObservableCollection<StemViewModel> Stems { get; } = new();

    public StemPlayerViewModel()
    {
        _audioService = new AudioPlaybackService();
        _audioService.PositionChanged += OnPositionChanged;
        _audioService.PlaybackStopped += OnPlaybackStopped;
    }

    public void LoadStems(Dictionary<StemType, string> localPaths)
    {
        _audioService.LoadStems(localPaths);
        Duration = _audioService.Duration;
        DurationFormatted = TimeFormatter.Format(Duration);

        Stems.Clear();
        foreach (var (stemType, path) in localPaths)
        {
            var stemVm = new StemViewModel(stemType, path, this);
            Stems.Add(stemVm);
        }
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (IsPlaying)
        {
            _audioService.Pause();
            IsPlaying = false;
        }
        else
        {
            _audioService.Play();
            IsPlaying = true;
        }
    }

    public void Seek(double seconds)
    {
        _audioService.Seek(seconds);
        CurrentTime = seconds;
        CurrentTimeFormatted = TimeFormatter.Format(seconds);
    }

    public void SetVolume(StemType stem, float volume)
    {
        _audioService.SetVolume(stem, volume);
    }

    [RelayCommand]
    private void DownloadMix()
    {
        // Build muted map from current stem states
        var mutedStems = new Dictionary<StemType, bool>();
        foreach (var stem in Stems)
            mutedStems[stem.Type] = stem.IsMuted;

        // Build description of what's included
        var included = Stems.Where(s => !s.IsMuted).Select(s => s.DisplayName);
        var description = string.Join(" + ", included);

        var dialog = new SaveFileDialog
        {
            Title = $"Save Mix ({description})",
            FileName = "mix.wav",
            Filter = "WAV Audio|*.wav",
            DefaultExt = ".wav"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                _audioService.ExportMix(dialog.FileName, mutedStems);
                System.Windows.MessageBox.Show(
                    $"Mix saved successfully!\n\nIncluded: {description}",
                    "Export Complete",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to export mix: {ex.Message}",
                    "Export Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void KaraokeMode()
    {
        // Mute vocals, unmute all others
        foreach (var stem in Stems)
        {
            if (stem.Type == StemType.Vocals)
            {
                if (!stem.IsMuted)
                {
                    stem.ToggleMuteCommand.Execute(null);
                }
            }
            else
            {
                if (stem.IsMuted)
                {
                    stem.ToggleMuteCommand.Execute(null);
                }
            }
        }

        // Trigger download mix with current mute state
        DownloadMix();
    }

    private void OnPositionChanged(double position)
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            CurrentTime = position;
            CurrentTimeFormatted = TimeFormatter.Format(position);
        });
    }

    private void OnPlaybackStopped()
    {
        System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
        {
            IsPlaying = false;
        });
    }

    public void Dispose()
    {
        _audioService.PositionChanged -= OnPositionChanged;
        _audioService.PlaybackStopped -= OnPlaybackStopped;
        _audioService.Dispose();
    }
}
