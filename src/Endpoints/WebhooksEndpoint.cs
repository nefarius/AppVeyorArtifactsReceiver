using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AppVeyorArtifactsReceiver.Endpoints;

/// <summary>
///     Endpoint listening for incoming webhook requests.
/// </summary>
internal sealed class WebhooksEndpoint(IOptions<ServiceConfig> serviceConfig, ILogger<WebhooksEndpoint> logger)
    : Endpoint<WebhookRequest>
{
    public override void Configure()
    {
        Post("/webhooks/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(WebhookRequest req, CancellationToken ct)
    {
        logger.LogDebug("Received webhook request for {Id}", req.Id);

        if (!serviceConfig.Value.Webhooks.Any(kvp => Equals(Guid.Parse(kvp.Key), req.Id)))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        Mode waitMode = Mode.WaitForNone;

        // solves the rate limit on artifact download URLs coming from GitHub actions
        if (HttpContext.Request.Headers.TryGetValue("X-GitHub-Token", out StringValues githubToken))
        {
            req.GitHubToken = githubToken;
            // postpone token expiration until we're done downloading
            waitMode = Mode.WaitForAll;
        }

        await PublishAsync(req, waitMode, ct);
        await Send.OkAsync("OK", ct);
    }
}