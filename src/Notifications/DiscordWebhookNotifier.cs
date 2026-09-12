#nullable enable
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
