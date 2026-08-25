namespace StemSeparator.Utilities;

public static class TimeFormatter
{
    public static string Format(double totalSeconds)
    {
        if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0)
            return "0:00";

        var minutes = (int)(totalSeconds / 60);
        var seconds = (int)(totalSeconds % 60);
        return $"{minutes}:{seconds:D2}";
    }
}
