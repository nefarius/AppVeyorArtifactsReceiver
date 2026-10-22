using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.EventHandlers;
using AppVeyorArtifactsReceiver.Metadata;
using AppVeyorArtifactsReceiver.Models;
using AppVeyorArtifactsReceiver.Notifications;

using Microsoft.Extensions.Logging;
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
    public async Task Opted_out_valid_zip_is_accepted_when_metadata_is_disabled()
    {
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync(
            "true",
            storeMetaData: false,
            latestSymlinkTemplate: "latest");

        Assert.True(File.Exists(fixture.ArchivePath));
        Assert.False(File.Exists(fixture.ExtractedExecutablePath));
        Assert.False(File.Exists(fixture.SidecarPath));
        Assert.DoesNotContain(
            fixture.Errors,
            message => message.Contains("invalid central directory", StringComparison.OrdinalIgnoreCase));
        Assert.True(
            Directory.Exists(fixture.LatestPath) ||
            fixture.Errors.Any(message => message.Contains("Failed to create symbolic link", StringComparison.Ordinal)),
            string.Join(" | ", fixture.Errors));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Opted_out_zip_with_invalid_central_directory_fails_and_does_not_retarget_latest(
        bool storeMetaData)
    {
        byte[] truncatedZip = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync(
            "true",
            body: truncatedZip,
            storeMetaData: storeMetaData,
            latestSymlinkTemplate: "latest");

        Assert.False(File.Exists(fixture.ArchivePath));
        Assert.False(File.Exists(fixture.ExtractedExecutablePath));
        Assert.False(Directory.Exists(fixture.LatestPath));
        Assert.Empty(Directory.EnumerateFiles(fixture.BuildDirectory, "*.incoming", SearchOption.AllDirectories));
        Assert.Contains(
            fixture.Errors,
            message => message.Contains("invalid central directory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            fixture.Errors,
            message => message.Contains(
                "Skipping latest symlink update because a retained GitHub artifact ZIP failed central-directory validation",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_retained_zip_does_not_replace_a_published_artifact()
    {
        await using HandlerFixture fixture = await HandlerFixture.CreateAsync(
            "true",
            storeMetaData: false,
            latestSymlinkTemplate: "latest");
        byte[] published = await File.ReadAllBytesAsync(fixture.ArchivePath);

        fixture.RecordingHandler.Body = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
        await fixture.DeliverAsync();

        Assert.Equal(published, await File.ReadAllBytesAsync(fixture.ArchivePath));
        Assert.True(
            fixture.Errors.Any(message => message.Contains("invalid central directory", StringComparison.OrdinalIgnoreCase)),
            string.Join("\n", fixture.Errors));
        Assert.Contains(
            fixture.Errors,
            message => message.Contains(
                "Skipping latest symlink update because a retained GitHub artifact ZIP failed central-directory validation",
                StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(fixture.BuildDirectory, "*.incoming", SearchOption.AllDirectories));
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

        private readonly WebhookReceivedEventHandler _handler;
        private readonly Guid _webhookId;
        private readonly Dictionary<string, string> _environment;

        private HandlerFixture(
            string root,
            RecordingHandler handler,
            WebhookReceivedEventHandler eventHandler,
            Guid webhookId,
            Dictionary<string, string> environment)
        {
            _root = root;
            _handler = eventHandler;
            _webhookId = webhookId;
            _environment = environment;
            RecordingHandler = handler;
        }

        public RecordingHandler RecordingHandler { get; }

        public AuthenticationHeaderValue? Authorization => RecordingHandler.Authorization;

        public string BuildDirectory => Path.Combine(_root, "builds", "MultiPadTester");

        public string ArchivePath => Path.Combine(BuildDirectory, "MultiPadTester.zip");

        public string ExtractedExecutablePath => Path.Combine(BuildDirectory, "MultiPadTester.exe");

        public string SidecarPath => Path.Combine(BuildDirectory, ".MultiPadTester", ".MultiPadTester.exe.json");

        public string LatestPath => Path.Combine(_root, "latest");

        public IReadOnlyList<string> Errors => RecordingHandler.Logger.Errors;

        public static async Task<HandlerFixture> CreateAsync(
            string? skipZipExtraction,
            string? gitHubToken = "ci-token",
            byte[]? body = null,
            bool storeMetaData = true,
            string? latestSymlinkTemplate = null)
        {
            string root = Path.Combine(Path.GetTempPath(), "avar-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            byte[] payload = body ?? CreateZip(GetVersionedPeBytes());
            RecordingHandler recordingHandler = new(payload);

            Guid webhookId = Guid.NewGuid();
            ServiceConfig config = new()
            {
                Webhooks = new Dictionary<string, TargetSettings>
                {
                    [webhookId.ToString()] = new TargetSettings
                    {
                        RootDirectory = root,
                        TargetPathTemplate = "builds/{appveyor_project_name}",
                        LatestSymlinkTemplate = latestSymlinkTemplate,
                        StoreMetaData = storeMetaData
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
                recordingHandler.Logger,
                new StaticOptions(config),
                new SingleClientFactory(recordingHandler),
                new DiscordWebhookNotifier(
                    new SingleClientFactory(recordingHandler),
                    NullLogger<DiscordWebhookNotifier>.Instance),
                new UnusedMsiReader());

            HandlerFixture fixture = new(root, recordingHandler, handler, webhookId, environment)
            {
                GitHubToken = gitHubToken
            };
            await fixture.DeliverAsync();
            return fixture;
        }

        public string? GitHubToken { get; init; }

        public Task DeliverAsync()
        {
            return _handler.HandleAsync(new WebhookRequest
            {
                Id = _webhookId,
                GitHubToken = GitHubToken,
                Artifacts =
                [
                    new Artifact
                    {
                        FileName = "MultiPadTester.zip",
                        Url = "https://example.test/artifact.zip"
                    }
                ],
                EnvironmentVariables = _environment
            }, CancellationToken.None);
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
        public byte[] Body { get; set; } = body;

        public ListLogger Logger { get; } = new();

        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Body)
            });
        }
    }

    private sealed class ListLogger : ILogger<WebhookReceivedEventHandler>
    {
        public List<string> Errors { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                string message = formatter(state, exception);
                if (exception != null)
                {
                    message += $" :: {exception.GetType().Name}: {exception.Message}";
                }

                Errors.Add(message);
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, disposeHandler: false);
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
