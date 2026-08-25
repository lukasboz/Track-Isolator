namespace StemSeparator.Models;

public enum StemType
{
    Vocals,
    Drums,
    Bass,
    Other
}

public static class StemTypeExtensions
{
    public static string DisplayName(this StemType stem) => stem switch
    {
        StemType.Vocals => "Vocals",
        StemType.Drums => "Drums",
        StemType.Bass => "Bass",
        StemType.Other => "Other Instruments",
        _ => stem.ToString()
    };

    public static string ApiName(this StemType stem) => stem switch
    {
        StemType.Vocals => "vocals",
        StemType.Drums => "drums",
        StemType.Bass => "bass",
        StemType.Other => "other",
        _ => stem.ToString().ToLower()
    };

    public static string AccentColor(this StemType stem) => stem switch
    {
        StemType.Vocals => "#c084fc",  // purple-light
        StemType.Drums => "#f97316",   // orange
        StemType.Bass => "#22d3ee",    // cyan
        StemType.Other => "#4ade80",   // green
        _ => "#9ca3af"
    };
}
