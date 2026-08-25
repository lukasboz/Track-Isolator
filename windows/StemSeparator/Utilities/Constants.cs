namespace StemSeparator.Utilities;

public static class Constants
{
    public const int DefaultPort = 8000;
    public const string BackendExecutableName = "stem-separator-backend.exe";
    public const string BackendSubfolder = "backend";
    public const int HealthCheckIntervalMs = 500;
    public const int HealthCheckTimeoutMs = 120_000;  // 2 minutes for model loading
    public const int HttpTimeoutMinutes = 10;
    public const int PositionUpdateIntervalMs = 50;
    public const long MaxFileBytes = 50L * 1024 * 1024;

    public static readonly string[] SupportedExtensions =
        { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".aif", ".aiff" };

    public static readonly string FileFilter =
        "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac;*.aif;*.aiff|All Files|*.*";

    public static string GetBaseUrl(int port) => $"http://127.0.0.1:{port}";
}
