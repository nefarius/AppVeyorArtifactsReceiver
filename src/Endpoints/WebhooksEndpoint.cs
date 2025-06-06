using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver.Endpoints;

/// <summary>
///     Endpoint listening for incoming webhook requests.
/// </summary>
internal sealed class WebhooksEndpoint(IOptions<ServiceConfig> serviceConfig) : Endpoint<WebhookRequest>
{
    public override void Configure()
    {
        Post("/webhooks/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(WebhookRequest req, CancellationToken ct)
    {
        if (!serviceConfig.Value.Webhooks.Any(kvp => Equals(Guid.Parse(kvp.Key), req.Id)))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await PublishAsync(req, Mode.WaitForNone, ct);
        await SendOkAsync(ct);
    }
}