using System.Text.RegularExpressions;

using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver.EventHandlers;

/// <summary>
///     Service that processes received artifacts and stores them on disk.
/// </summary>
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

        // each job can have multiple artifacts
        foreach (Artifact artifact in req.Artifacts)
        {
            string absolutePath = Path.Combine(hookCfg.RootDirectory, subDirectory, artifact.FileName);

            _logger.LogInformation("Sub-path for artifact {FileName}: {Path}",
                artifact.FileName, subDirectory);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

            using HttpClient httpClient = _httpClientFactory.CreateClient("AppVeyor");

            await using Stream stream = await httpClient.GetStreamAsync(artifact.Url, ct);

            await using FileStream file = File.Create(absolutePath);

            await stream.CopyToAsync(file, ct);
        }

        // updates the "latest" special directory symlink with the most up-to-date target
        if (!string.IsNullOrEmpty(hookCfg.TargetPathTemplate))
        {
            string latestSubDirectory = Replace(hookCfg.LatestSymlinkTemplate, req.EnvironmentVariables);
            string absoluteSymlinkPath = Path.Combine(hookCfg.RootDirectory, latestSubDirectory);

            try
            {
                if (Directory.Exists(absoluteSymlinkPath))
                {
                    Directory.Delete(absoluteSymlinkPath);
                }

                FileSystemInfo linkInfo = File.CreateSymbolicLink(absoluteSymlinkPath, absoluteTargetPath);
                _logger.LogInformation("Created/updated symbolic link {Link}", linkInfo);

                string timestampFileAbsolutePath = Path.Combine(absoluteTargetPath, "LAST_UPDATED_AT.txt");
                await using StreamWriter tsFile = File.CreateText(timestampFileAbsolutePath);
                await tsFile.WriteAsync(DateTime.UtcNow.ToString("O"));
                tsFile.Close();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create symbolic link");
            }
        }
    }

    [GeneratedRegex("{(?<placeholder>[a-z_][a-z0-9_]*?)}", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PlaceholdersRegex();

    /// <summary>
    ///     Replaces placeholder tokens (like {appveyor_project_name}) with the variable content of the payload.
    /// </summary>
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