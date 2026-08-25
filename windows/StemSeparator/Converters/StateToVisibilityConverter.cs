using System.Globalization;
using System.Windows;
using System.Windows.Data;
using StemSeparator.Models;

namespace StemSeparator.Converters;

/// <summary>
/// Converts AppState to Visibility. Pass the target state(s) as parameter.
/// Supports comma-separated states, e.g. "Uploading,Processing".
/// </summary>
public class StateToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not AppState state || parameter is not string stateNames)
            return Visibility.Collapsed;

        var targets = stateNames.Split(',', StringSplitOptions.TrimEntries);
        var currentName = state.ToString();

        return targets.Contains(currentName, StringComparer.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
