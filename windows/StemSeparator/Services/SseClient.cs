using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using StemSeparator.Models;

namespace StemSeparator.Services;

public static class SseClient
{
    public static async IAsyncEnumerable<SSEEvent> ReadEventsAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
                break;

            // SSE spec: empty lines are event delimiters; skip them
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // We only care about "data: " lines
            if (!line.StartsWith("data: "))
                continue;

            var json = line["data: ".Length..];

            SSEEvent? sseEvent;
            try
            {
                sseEvent = JsonSerializer.Deserialize<SSEEvent>(json);
            }
            catch (JsonException)
            {
                // Malformed JSON line — skip
                continue;
            }

            if (sseEvent is not null)
                yield return sseEvent;
        }
    }
}
