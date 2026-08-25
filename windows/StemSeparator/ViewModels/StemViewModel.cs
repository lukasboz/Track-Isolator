using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StemSeparator.Models;

namespace StemSeparator.ViewModels;

public partial class StemViewModel : ObservableObject
{
    private readonly StemPlayerViewModel _parent;
    private readonly string _localPath;

    public StemType Type { get; }
    public string DisplayName { get; }
    public string AccentColor { get; }

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private float _volume = 1.0f;

    /// <summary>Volume as 0-100 for slider binding.</summary>
    public float VolumePercent
    {
        get => Volume * 100f;
        set { Volume = value / 100f; OnPropertyChanged(); }
    }

    /// <summary>
    /// Stores the volume value before muting, so we can restore it on unmute.
    /// </summary>
    private float _preMuteVolume = 1.0f;

    public StemViewModel(StemType type, string localPath, StemPlayerViewModel parent)
    {
        Type = type;
        _localPath = localPath;
        _parent = parent;
        DisplayName = type.DisplayName();
        AccentColor = type.AccentColor();
    }

    partial void OnVolumeChanged(float value)
    {
        // When the user adjusts the slider while not muted, apply the volume
        if (!IsMuted)
        {
            _parent.SetVolume(Type, value);
        }
        // Also update pre-mute volume so unmuting restores the latest slider position
        _preMuteVolume = value;
    }

    [RelayCommand]
    private void ToggleMute()
    {
        IsMuted = !IsMuted;
        if (IsMuted)
        {
            _preMuteVolume = Volume;
            _parent.SetVolume(Type, 0f);
        }
        else
        {
            _parent.SetVolume(Type, _preMuteVolume);
        }
    }

    [RelayCommand]
    private void Download()
    {
        var dialog = new SaveFileDialog
        {
            Title = $"Save {DisplayName} Stem",
            FileName = $"{Type.ApiName()}.wav",
            Filter = "WAV Audio|*.wav",
            DefaultExt = ".wav"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                File.Copy(_localPath, dialog.FileName, overwrite: true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to save file: {ex.Message}",
                    "Save Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
