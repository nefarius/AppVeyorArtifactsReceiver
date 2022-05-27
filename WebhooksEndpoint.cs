using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver;

public class WebhooksEndpoint : Endpoint<Root>
{
    private readonly IOptions<ServiceConfig> _serviceConfig;

    private readonly IHttpClientFactory _httpClientFactory;

    private readonly ILogger<WebhooksEndpoint> _logger;

    public WebhooksEndpoint(IOptions<ServiceConfig> serviceConfig, IHttpClientFactory httpClientFactory, ILogger<WebhooksEndpoint> logger)
    {
        _serviceConfig = serviceConfig;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/webhooks/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(Root req, CancellationToken ct)
    {
        if (!_serviceConfig.Value.Webhooks.Any(kvp => Equals(Guid.Parse(kvp.Key), req.Id)))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var hookCfg = _serviceConfig.Value.Webhooks
            .First(kvp => Equals(Guid.Parse(kvp.Key), req.Id)).Value;

        var subDirectory = Replace(hookCfg.TargetPathTemplate, req.EnvironmentVariables);
        
        _logger.LogInformation("Build sub-directory {Directory}", subDirectory);

        Directory.CreateDirectory(Path.Combine(hookCfg.RootDirectory, subDirectory));

        foreach (var artifact in req.Artifacts)
        {
            var absolutePath = Path.Combine(hookCfg.RootDirectory, subDirectory, artifact.FileName);

            _logger.LogInformation("Absolute path for artifact {Artifact}: {Path}",
                artifact.Name, subDirectory);

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));

            using var httpClient = _httpClientFactory.CreateClient();

            var stream = await httpClient.GetStreamAsync(artifact.Url, ct);

            await using var file = File.Create(absolutePath);

            await stream.CopyToAsync(file, ct);
        }

        await SendOkAsync(ct);
    }

    private static string Replace(string input, IReadOnlyDictionary<string, string> replacement)
    {
        var regex = new Regex("{(?<placeholder>[a-z_][a-z0-9_]*?)}",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        return regex.Replace(input, m =>
        {
            var key = m.Groups["placeholder"].Value;
            if (replacement.TryGetValue(key, out var value))
                return value;

            throw new Exception($"Unknown key {key}");
        });
    }
}