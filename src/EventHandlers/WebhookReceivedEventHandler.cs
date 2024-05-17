using System.Text.RegularExpressions;

using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver.EventHandlers;

internal sealed partial class WebhookReceivedEventHandler : IEventHandler<WebhookRequest>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookReceivedEventHandler> _logger;
    private readonly IOptions<ServiceConfig> _serviceConfig;

    public WebhookReceivedEventHandler(ILogger<WebhookReceivedEventHandler> logger,
        IOptions<ServiceConfig> serviceConfig, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _serviceConfig = serviceConfig;
        _httpClientFactory = httpClientFactory;
    }

    public async Task HandleAsync(WebhookRequest req, CancellationToken ct)
    {
        TargetSettings hookCfg = _serviceConfig.Value.Webhooks
            .First(kvp => Equals(Guid.Parse(kvp.Key), req.Id)).Value;

        string subDirectory = Replace(hookCfg.TargetPathTemplate, req.EnvironmentVariables);

        _logger.LogInformation("Build sub-directory {Directory}", subDirectory);

        string absoluteTargetPath = Path.Combine(hookCfg.RootDirectory, subDirectory);
        Directory.CreateDirectory(absoluteTargetPath);

        foreach (Artifact artifact in req.Artifacts)
        {
            string absolutePath = Path.Combine(hookCfg.RootDirectory, subDirectory, artifact.FileName);

            _logger.LogInformation("Sub-path for artifact {FileName}: {Path}",
                artifact.FileName, subDirectory);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

            HttpClient httpClient = _httpClientFactory.CreateClient("AppVeyor");

            Stream stream = await httpClient.GetStreamAsync(artifact.Url, ct);

            await using FileStream file = File.Create(absolutePath);

            await stream.CopyToAsync(file, ct);
        }

        if (!string.IsNullOrEmpty(hookCfg.TargetPathTemplate))
        {
            string latestSubDirectory = Replace(hookCfg.LatestSymlinkTemplate, req.EnvironmentVariables);
            string absoluteSymlinkPath = Path.Combine(hookCfg.RootDirectory, latestSubDirectory);

            try
            {
                if (File.Exists(absoluteSymlinkPath))
                {
                    File.Delete(absoluteSymlinkPath);
                }

                FileSystemInfo linkInfo = File.CreateSymbolicLink(absoluteSymlinkPath, absoluteTargetPath);
                _logger.LogInformation("Created/updated symbolic link {Link}", linkInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create symbolic link");
            }
        }
    }

    [GeneratedRegex("{(?<placeholder>[a-z_][a-z0-9_]*?)}", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PlaceholdersRegex();

    private static string Replace(string input, IReadOnlyDictionary<string, string> replacement)
    {
        return PlaceholdersRegex().Replace(input, m =>
        {
            string key = m.Groups["placeholder"].Value;
            if (replacement.TryGetValue(key, out string value))
            {
                return value;
            }

            throw new Exception($"Unknown key {key}");
        });
    }
}