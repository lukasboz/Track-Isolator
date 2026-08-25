using System.IO;
namespace StemSeparator.Utilities;

public static class FileValidator
{
    public static bool IsAudioFile(string filePath)
    {
        return GetValidationError(filePath) is null;
    }

    public static bool IsAudioFile(string[] filePaths)
    {
        return filePaths.Length == 1 && IsAudioFile(filePaths[0]);
    }

    public static string? GetValidationError(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return "Select an audio file that exists.";

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (!Constants.SupportedExtensions.Contains(extension))
        {
            return "Unsupported audio type. Choose MP3, WAV, FLAC, OGG, M4A, AAC, AIF, or AIFF.";
        }

        var size = new FileInfo(filePath).Length;
        if (size <= 0)
            return "The selected audio file is empty.";
        if (size > Constants.MaxFileBytes)
            return $"The selected file is {size / 1024d / 1024d:F1} MB. The limit is 50 MB.";

        return null;
    }
}
