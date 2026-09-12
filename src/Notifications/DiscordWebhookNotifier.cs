#nullable enable
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;

using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Logging;

namespace AppVeyorArtifactsReceiver.Notifications;

/// <summary>
///     Posts one job-summary embed to every configured Discord incoming-webhook URL.
///     Delivery is best-effort and never throws to the caller.
/// </summary>
internal sealed class DiscordWebhookNotifier(
    IHttpClientFactory httpClientFactory,
    ILogger<DiscordWebhookNotifier> logger)
{
    public const string HttpClientName = "Discord";

    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

    internal static readonly TimeSpan DuplicateWindow = TimeSpan.FromHours(6);

    private readonly ConcurrentDictionary<string, long> _recentNotifications = new();

    public async Task NotifyAsync(
        IEnumerable<string>? webhookUrls,
        WebhookRequest request,
        JobProcessingResult result,
        CancellationToken ct,
        string? publicArtifactsBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        List<string> urls = NormalizeUrls(webhookUrls);
        if (urls.Count == 0)
        {
            return;
        }

        if (!TryClaimNotification(request, result))
        {
            logger.LogInformation("Skipping duplicate Discord notification for this run and target");
            return;
        }

        byte[] body = Encoding.UTF8.GetBytes(
            DiscordNotificationBuilder.Serialize(
                DiscordNotificationBuilder.Build(request, result, publicArtifactsBaseUrl)));

        using HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        Task[] posts = new Task[urls.Count];
        for (int i = 0; i < urls.Count; i++)
        {
            posts[i] = PostAsync(client, urls[i], i + 1, body, ct);
        }

        await Task.WhenAll(posts);
    }

    private async Task PostAsync(
        HttpClient client,
        string url,
        int index,
        byte[] body,
        CancellationToken ct)
    {
        try
        {
            using ByteArrayContent content = new(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using HttpResponseMessage response = await client.PostAsync(url, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Discord notification {Index} returned HTTP {StatusCode}",
                    index, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to deliver Discord notification {Index}", index);
        }
    }

    internal static string? TryCreateDedupeKey(WebhookRequest request, JobProcessingResult result)
    {
        if (string.IsNullOrWhiteSpace(result.TargetSubDirectory))
        {
            return null;
        }

        string? runId = GetEnv(request, "github_run_id");
        if (string.IsNullOrWhiteSpace(runId) && request.BuildId > 0)
        {
            runId = request.BuildId.ToString();
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            runId = GetEnv(request, "appveyor_build_id");
        }

        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        return $"{request.Id:N}|{runId.Trim()}|{result.TargetSubDirectory.Trim()}";
    }

    private static string? GetEnv(WebhookRequest request, string key)
    {
        if (request.EnvironmentVariables is null)
        {
            return null;
        }

        return request.EnvironmentVariables.TryGetValue(key, out string? value) ? value : null;
    }

    private bool TryClaimNotification(WebhookRequest request, JobProcessingResult result)
    {
        string? key = TryCreateDedupeKey(request, result);
        if (key is null)
        {
            return true;
        }

        long now = DateTime.UtcNow.Ticks;
        long window = DuplicateWindow.Ticks;
        EvictExpired(now, window);

        while (true)
        {
            if (_recentNotifications.TryGetValue(key, out long previous))
            {
                if (now - previous < window)
                {
                    return false;
                }

                if (_recentNotifications.TryUpdate(key, now, previous))
                {
                    return true;
                }

                continue;
            }

            if (_recentNotifications.TryAdd(key, now))
            {
                return true;
            }
        }
    }

    private void EvictExpired(long now, long window)
    {
        foreach (KeyValuePair<string, long> entry in _recentNotifications)
        {
            if (now - entry.Value >= window)
            {
                _recentNotifications.TryRemove(entry);
            }
        }
    }

    internal static List<string> NormalizeUrls(IEnumerable<string>? webhookUrls)
    {
        if (webhookUrls is null)
        {
            return [];
        }

        return webhookUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
