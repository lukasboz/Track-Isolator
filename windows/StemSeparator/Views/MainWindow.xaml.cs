using System.Windows;
using System.Windows.Input;
using StemSeparator.Models;
using StemSeparator.ViewModels;

namespace StemSeparator.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.MainViewModel;
        KeyDown += MainWindow_KeyDown;
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        var vm = App.MainViewModel;

        // Only handle shortcuts when in Ready state
        if (vm.CurrentState != AppState.Ready || vm.Player is null)
            return;

        switch (e.Key)
        {
            case Key.Space:
                vm.Player.TogglePlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.D1:
            case Key.NumPad1:
                ToggleStemMute(vm.Player, StemType.Vocals);
                e.Handled = true;
                break;

            case Key.D2:
            case Key.NumPad2:
                ToggleStemMute(vm.Player, StemType.Drums);
                e.Handled = true;
                break;

            case Key.D3:
            case Key.NumPad3:
                ToggleStemMute(vm.Player, StemType.Bass);
                e.Handled = true;
                break;

            case Key.D4:
            case Key.NumPad4:
                ToggleStemMute(vm.Player, StemType.Other);
                e.Handled = true;
                break;

            case Key.Left:
                SeekRelative(vm.Player, -5);
                e.Handled = true;
                break;

            case Key.Right:
                SeekRelative(vm.Player, 5);
                e.Handled = true;
                break;

            case Key.M:
                MuteAll(vm.Player);
                e.Handled = true;
                break;

            case Key.U:
                UnmuteAll(vm.Player);
                e.Handled = true;
                break;
        }
    }

    private static void ToggleStemMute(StemPlayerViewModel player, StemType stemType)
    {
        var stem = player.Stems.FirstOrDefault(s => s.Type == stemType);
        stem?.ToggleMuteCommand.Execute(null);
    }

    private static void SeekRelative(StemPlayerViewModel player, double offsetSeconds)
    {
        var newTime = Math.Clamp(player.CurrentTime + offsetSeconds, 0, player.Duration);
        player.Seek(newTime);
    }

    private static void MuteAll(StemPlayerViewModel player)
    {
        foreach (var stem in player.Stems)
        {
            if (!stem.IsMuted)
                stem.ToggleMuteCommand.Execute(null);
        }
    }

    private static void UnmuteAll(StemPlayerViewModel player)
    {
        foreach (var stem in player.Stems)
        {
            if (stem.IsMuted)
                stem.ToggleMuteCommand.Execute(null);
        }
    }
}
