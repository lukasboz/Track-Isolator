using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using StemSeparator.ViewModels;

namespace StemSeparator.Views;

public partial class StemPlayerView : UserControl
{
    public StemPlayerView()
    {
        InitializeComponent();
    }

    private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        // Seeking is committed when the thumb is released.
    }

    private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Player != null)
        {
            vm.Player.Seek(SeekSlider.Value);
        }
    }
}
