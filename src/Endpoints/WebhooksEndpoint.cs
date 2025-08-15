using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver.Endpoints;

/// <summary>
///     Endpoint listening for incoming webhook requests.
/// </summary>
internal sealed class WebhooksEndpoint(IOptions<ServiceConfig> serviceConfig, ILogger<WebhooksEndpoint> logger) : Endpoint<WebhookRequest>
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

        await PublishAsync(req, Mode.WaitForNone, ct);
        await Send.OkAsync(ct);
    }
}