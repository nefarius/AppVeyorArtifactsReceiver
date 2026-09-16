using System.Net;
using System.Net.Http.Headers;

using AppVeyorArtifactsReceiver.Models;
using AppVeyorArtifactsReceiver.Notifications;

using Microsoft.Extensions.Logging.Abstractions;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class DiscordWebhookNotifierTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NotifyAsync_does_nothing_when_no_usable_urls_are_configured(string? url)
    {
        RecordingHandler handler = new();
        DiscordWebhookNotifier notifier = CreateNotifier(handler);

        IEnumerable<string>? urls = url is null ? null : [url];
        await notifier.NotifyAsync(urls, CreateRequest(), CreateSuccessResult(), CancellationToken.None);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NotifyAsync_posts_the_same_payload_to_every_configured_url()
    {
        RecordingHandler handler = new();
        DiscordWebhookNotifier notifier = CreateNotifier(handler);
        string[] urls =
        [
            "https://discord.com/api/webhooks/1/alpha",
            "https://discord.com/api/webhooks/2/beta"
        ];

        await notifier.NotifyAsync(urls, CreateRequest(), CreateSuccessResult(), CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(
            urls.OrderBy(url => url, StringComparer.Ordinal),
            handler.Requests.Select(call => call.Url).OrderBy(url => url, StringComparer.Ordinal));
        Assert.All(handler.Requests, call =>
        {
            Assert.Equal(HttpMethod.Post, call.Method);
            Assert.Equal("application/json", call.ContentType?.MediaType);
            Assert.Contains("Artifacts received", call.Body, StringComparison.Ordinal);
            Assert.Contains("\"parse\":[]", call.Body.Replace(" ", string.Empty), StringComparison.Ordinal);
        });
        Assert.Equal(handler.Requests[0].Body, handler.Requests[1].Body);
    }

    [Fact]
    public async Task NotifyAsync_continues_when_one_endpoint_fails()
    {
        RecordingHandler handler = new(request =>
        {
            if (request.RequestUri!.AbsoluteUri.Contains("/1/", StringComparison.Ordinal))
            {
                throw new HttpRequestException("webhook rejected");
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });
        DiscordWebhookNotifier notifier = CreateNotifier(handler);

        await notifier.NotifyAsync(
            [
                "https://discord.com/api/webhooks/1/fail",
                "https://discord.com/api/webhooks/2/ok"
            ],
            CreateRequest(),
            CreateSuccessResult(),
            CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, call => call.Url.Contains("/2/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NotifyAsync_does_not_replay_a_transient_http_failure()
    {
        RecordingHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        DiscordWebhookNotifier notifier = CreateNotifier(handler);

        await notifier.NotifyAsync(
            ["https://discord.com/api/webhooks/1/token"],
            CreateRequest(),
            CreateSuccessResult(),
            CancellationToken.None);

        RecordedRequest posted = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, posted.Method);
    }

    [Fact]
    public async Task NotifyAsync_posts_again_for_the_same_run_and_target()
    {
        RecordingHandler handler = new();
        DiscordWebhookNotifier notifier = CreateNotifier(handler);
        WebhookRequest request = CreateRequest();
        request.EnvironmentVariables = new Dictionary<string, string>
        {
            ["github_run_id"] = "33663544790"
        };
        JobProcessingResult result = CreateSuccessResult();
        result.TargetSubDirectory = "builds/DsHidMini/v3.7.2/251";
        string[] urls = ["https://discord.com/api/webhooks/1/token"];

        await notifier.NotifyAsync(urls, request, result, CancellationToken.None);
        await notifier.NotifyAsync(urls, request, result, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public void NormalizeUrls_skips_blanks_and_duplicates()
    {
        List<string> urls = DiscordWebhookNotifier.NormalizeUrls(
        [
            " https://discord.com/api/webhooks/1/token ",
            string.Empty,
            "https://discord.com/api/webhooks/1/token",
            "   ",
            "https://discord.com/api/webhooks/2/other"
        ]);

        Assert.Equal(
            [
                "https://discord.com/api/webhooks/1/token",
                "https://discord.com/api/webhooks/2/other"
            ],
            urls);
    }

    private static DiscordWebhookNotifier CreateNotifier(RecordingHandler handler)
    {
        return new DiscordWebhookNotifier(new FixedHttpClientFactory(handler), NullLogger<DiscordWebhookNotifier>.Instance);
    }

    private static WebhookRequest CreateRequest()
    {
        return new WebhookRequest
        {
            ProjectName = "DsHidMini",
            BuildVersion = "1.0.0",
            Branch = "master",
            CommitId = "abc1234",
            Artifacts = [],
            EnvironmentVariables = []
        };
    }

    private static JobProcessingResult CreateSuccessResult()
    {
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();
        return result;
    }

    private sealed class FixedHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(DiscordWebhookNotifier.HttpClientName, name);
            return new HttpClient(handler, disposeHandler: false)
            {
                BaseAddress = new Uri("https://discord.com")
            };
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        private readonly List<RecordedRequest> _requests = [];
        private readonly object _gate = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
        {
            _responder = responder ?? (_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RecordedRequest recorded = new(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? string.Empty,
                body,
                request.Content?.Headers.ContentType);

            lock (_gate)
            {
                _requests.Add(recorded);
            }

            return _responder(request);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Url,
        string Body,
        MediaTypeHeaderValue? ContentType);
}
