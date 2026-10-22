using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.EventHandlers;
using AppVeyorArtifactsReceiver.Metadata;
using AppVeyorArtifactsReceiver.Models;
using AppVeyorArtifactsReceiver.Notifications;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class WebhookReceivedEventHandlerTests
{
    [Fact]
    public async Task Token_bearing_zip_is_extracted_when_opt_out_is_missing()
    {
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync(skipZipExtraction: null);

        Assert.False(File.Exists(fixture.ArchivePath));
        Assert.True(File.Exists(fixture.ExtractedExecutablePath));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("skip")]
    public async Task Token_bearing_zip_is_extracted_unless_opt_out_is_true(string skipValue)
    {
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync(skipValue);

        Assert.False(File.Exists(fixture.ArchivePath));
        Assert.True(File.Exists(fixture.ExtractedExecutablePath));
    }

    [Fact]
    public async Task Opted_out_github_zip_stays_packed_and_gets_nested_metadata()
    {
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync("true");

        Assert.True(File.Exists(fixture.ArchivePath));
        Assert.False(File.Exists(fixture.ExtractedExecutablePath));
        Assert.Equal("Bearer", fixture.Authorization?.Scheme);
        Assert.Equal("ci-token", fixture.Authorization?.Parameter);

        using JsonDocument sidecar = JsonDocument.Parse(await File.ReadAllTextAsync(fixture.SidecarPath));
        Assert.Equal("pe", sidecar.RootElement.GetProperty("type").GetString());
        Assert.False(string.IsNullOrWhiteSpace(sidecar.RootElement.GetProperty("FileVersion").GetString()));
    }

    [Fact]
    public async Task Zip_without_github_token_stays_packed_even_when_opt_out_is_false()
    {
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync("false", gitHubToken: null);

        Assert.True(File.Exists(fixture.ArchivePath));
        Assert.False(File.Exists(fixture.ExtractedExecutablePath));
        Assert.Null(fixture.Authorization);
    }

    [Fact]
    public async Task Token_bearing_non_zip_stays_a_loose_file()
    {
        byte[] body = "plain-text"u8.ToArray();
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync(skipZipExtraction: null, body: body);

        Assert.True(File.Exists(fixture.ArchivePath));
        Assert.Equal(body, await File.ReadAllBytesAsync(fixture.ArchivePath));
        Assert.False(File.Exists(fixture.ExtractedExecutablePath));
    }

    private sealed class HandlerFixture : IAsyncDisposable
    {
        private readonly string _root;

        private HandlerFixture(string root, RecordingHandler handler)
        {
            _root = root;
            RecordingHandler = handler;
        }

        public RecordingHandler RecordingHandler { get; }

        public AuthenticationHeaderValue? Authorization => RecordingHandler.Authorization;

        public string BuildDirectory => Path.Combine(_root, "builds", "MultiPadTester");

        public string ArchivePath => Path.Combine(BuildDirectory, "MultiPadTester.zip");

        public string ExtractedExecutablePath => Path.Combine(BuildDirectory, "MultiPadTester.exe");

        public string SidecarPath => Path.Combine(BuildDirectory, ".MultiPadTester", ".MultiPadTester.exe.json");

        public static async Task<HandlerFixture> CreateAsync(
            string? skipZipExtraction,
            string? gitHubToken = "ci-token",
            byte[]? body = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "avar-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            byte[] payload = body ?? CreateZip(GetVersionedPeBytes());
            RecordingHandler recordingHandler = new(payload);
            HandlerFixture fixture = new(root, recordingHandler);

            Guid webhookId = Guid.NewGuid();
            ServiceConfig config = new()
            {
                Webhooks = new Dictionary<string, TargetSettings>
                {
                    [webhookId.ToString()] = new TargetSettings
                    {
                        RootDirectory = root,
                        TargetPathTemplate = "builds/{appveyor_project_name}",
                        StoreMetaData = true
                    }
                }
            };

            Dictionary<string, string> environment = new()
            {
                ["appveyor_project_name"] = "MultiPadTester"
            };
            if (skipZipExtraction != null)
            {
                environment[WebhookRequest.SkipZipExtractionEnvironmentVariable] = skipZipExtraction;
            }

            WebhookReceivedEventHandler handler = new(
                NullLogger<WebhookReceivedEventHandler>.Instance,
                new StaticOptions(config),
                new SingleClientFactory(recordingHandler),
                new DiscordWebhookNotifier(
                    new SingleClientFactory(recordingHandler),
                    NullLogger<DiscordWebhookNotifier>.Instance),
                new UnusedMsiReader());

            await handler.HandleAsync(new WebhookRequest
            {
                Id = webhookId,
                GitHubToken = gitHubToken,
                Artifacts =
                [
                    new Artifact
                    {
                        FileName = "MultiPadTester.zip",
                        Url = "https://example.test/artifact.zip"
                    }
                ],
                EnvironmentVariables = environment
            }, CancellationToken.None);

            return fixture;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }

            return ValueTask.CompletedTask;
        }

        private static byte[] GetVersionedPeBytes()
        {
            string location = typeof(HandlerFixture).Assembly.Location;
            return File.ReadAllBytes(location);
        }

        private static byte[] CreateZip(byte[] executable)
        {
            using MemoryStream stream = new();
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry entry = archive.CreateEntry("MultiPadTester.exe");
                using Stream entryStream = entry.Open();
                entryStream.Write(executable);
            }

            return stream.ToArray();
        }
    }

    private sealed class RecordingHandler(byte[] body) : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        private readonly HttpClient _client = new(handler, disposeHandler: false);

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    private sealed class StaticOptions(ServiceConfig value) : IOptionsSnapshot<ServiceConfig>
    {
        public ServiceConfig Value { get; } = value;

        public ServiceConfig Get(string? name)
        {
            return Value;
        }
    }

    private sealed class UnusedMsiReader : IMsiMetadataReader
    {
        public Task<MsiMetadataReadResult> ReadAsync(string packagePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(MsiMetadataReadResult.Failed("unused"));
        }
    }
}
