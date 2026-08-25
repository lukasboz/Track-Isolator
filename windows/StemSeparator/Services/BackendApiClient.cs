using System.IO;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using StemSeparator.Models;
using StemSeparator.Utilities;

namespace StemSeparator.Services;

public class BackendApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public BackendApiClient(int port)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(Constants.GetBaseUrl(port)),
            Timeout = TimeSpan.FromMinutes(Constants.HttpTimeoutMinutes)
        };
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SeparationResult> SeparateAudioAsync(
        string filePath,
        IProgress<SeparationProgress> progress,
        CancellationToken ct)
    {
        await using var fileStream = File.OpenRead(filePath);
        var fileName = Path.GetFileName(filePath);

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType =
            new MediaTypeHeaderValue(GetMimeType(fileName));
        content.Add(streamContent, "audio", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/separate-stream")
        {
            Content = content
        };

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Parse SSE events from the streaming response
        Dictionary<string, string>? stemUrls = null;

        await foreach (var sseEvent in SseClient.ReadEventsAsync(response, ct).ConfigureAwait(false))
        {
            if (sseEvent.Error is not null)
                throw new InvalidOperationException(
                    $"Backend separation error: {sseEvent.Error}");

            if (sseEvent.Status is not null)
            {
                Debug.WriteLine($"[BackendApiClient] Status: {sseEvent.Status}");
                progress.Report(new SeparationProgress(
                    sseEvent.Status,
                    sseEvent.Progress));
            }

            if (sseEvent.Stems is not null)
            {
                stemUrls = sseEvent.Stems;
            }
        }

        if (stemUrls is null || stemUrls.Count == 0)
            throw new InvalidOperationException(
                "Separation completed but no stem URLs were received.");

        // Extract request_id from the first stem URL path
        // Format: /outputs/{request_id}/filename.wav
        var firstStemUrl = stemUrls.Values.First();
        var segments = firstStemUrl.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);
        if (
            segments.Length < 3 ||
            !segments[0].Equals("outputs", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(segments[1], out _))
        {
            throw new InvalidOperationException(
                "The backend returned an invalid output path.");
        }
        var requestId = segments[1];

        // Download each stem to a local temp directory
        var localDir = Path.Combine(Path.GetTempPath(), "StemSeparator", requestId);
        Directory.CreateDirectory(localDir);

        var result = new SeparationResult
        {
            RequestId = requestId,
            LocalDirectory = localDir,
            StemPaths = new Dictionary<StemType, string>()
        };

        try
        {
            var downloadTasks = stemUrls.Select(async entry =>
            {
                if (!Enum.TryParse<StemType>(
                        entry.Key,
                        ignoreCase: true,
                        out var stemType))
                {
                    Debug.WriteLine(
                        $"[BackendApiClient] Unknown stem name '{entry.Key}', skipping.");
                    return ((StemType Type, string Path)?)null;
                }

                var localPath = await DownloadStemAsync(
                    entry.Value,
                    localDir,
                    ct).ConfigureAwait(false);
                return (stemType, localPath);
            });

            var downloadedStems = await Task.WhenAll(downloadTasks).ConfigureAwait(false);
            foreach (var downloaded in downloadedStems)
            {
                if (downloaded is { } stem)
                    result.StemPaths[stem.Type] = stem.Path;
            }

            var missing = Enum.GetValues<StemType>()
                .Where(type => !result.StemPaths.ContainsKey(type))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The backend did not return all expected stems: {string.Join(", ", missing)}.");
            }

            return result;
        }
        catch
        {
            DeleteLocalFiles(result);
            throw;
        }
    }

    public async Task<string> DownloadStemAsync(
        string stemPath,
        string localDir,
        CancellationToken ct)
    {
        // stemPath is a relative URL like /outputs/{id}/vocals.wav
        var fileName = Path.GetFileName(stemPath);
        var localFilePath = Path.Combine(localDir, fileName);

        using var response = await _httpClient.GetAsync(
            stemPath,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var source = await response.Content
            .ReadAsStreamAsync(ct)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            localFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 1024 * 1024, ct).ConfigureAwait(false);

        Debug.WriteLine(
            $"[BackendApiClient] Downloaded {stemPath} -> {localFilePath}");

        return localFilePath;
    }

    public async Task CleanupAsync(
        SeparationResult result,
        CancellationToken ct = default)
    {
        try
        {
            await _httpClient.DeleteAsync(
                $"/cleanup/{result.RequestId}",
                ct).ConfigureAwait(false);
            Debug.WriteLine(
                $"[BackendApiClient] Cleaned up request {result.RequestId}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BackendApiClient] Cleanup failed for {result.RequestId}: {ex.Message}");
        }
        finally
        {
            DeleteLocalFiles(result);
        }
    }

    public static void DeleteLocalFiles(SeparationResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.LocalDirectory))
            return;

        try
        {
            if (Directory.Exists(result.LocalDirectory))
                Directory.Delete(result.LocalDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[BackendApiClient] Local cleanup failed for {result.LocalDirectory}: {ex.Message}");
        }
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".wma" => "audio/x-ms-wma",
            _ => "audio/mpeg"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
