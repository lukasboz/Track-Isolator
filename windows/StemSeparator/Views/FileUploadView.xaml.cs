using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StemSeparator.Utilities;
using StemSeparator.ViewModels;

namespace StemSeparator.Views;

public partial class FileUploadView : UserControl
{
    private static readonly SolidColorBrush HighlightBorder =
        new(Color.FromRgb(0xc0, 0x84, 0xfc)); // purple-light
    private static readonly SolidColorBrush NormalBorder =
        new(Color.FromRgb(0x4b, 0x55, 0x63));  // gray-600
    private static readonly SolidColorBrush HighlightBackground =
        new(Color.FromArgb(0x1A, 0x93, 0x33, 0xea)); // purple/10

    public FileUploadView()
    {
        InitializeComponent();
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length == 1 && FileValidator.IsAudioFile(files[0]))
            {
                DropZone.BorderBrush = HighlightBorder;
                DropZone.Background = HighlightBackground;
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }
        e.Handled = true;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Effects = files.Length == 1 && FileValidator.IsAudioFile(files[0])
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = NormalBorder;
        DropZone.Background = (SolidColorBrush)FindResource("CardInnerBrush");
        e.Handled = true;
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.BorderBrush = NormalBorder;
        DropZone.Background = (SolidColorBrush)FindResource("CardInnerBrush");

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (DataContext is MainViewModel vm)
            {
                vm.HandleFileDropCommand.Execute(files);
            }
        }
        e.Handled = true;
    }
}
