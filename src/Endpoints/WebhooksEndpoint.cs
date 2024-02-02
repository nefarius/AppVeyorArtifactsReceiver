using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using Microsoft.Extensions.Options;

namespace AppVeyorArtifactsReceiver.Endpoints;

internal sealed class WebhooksEndpoint : Endpoint<WebhookRequest>
{
    private readonly IOptions<ServiceConfig> _serviceConfig;

    public WebhooksEndpoint(IOptions<ServiceConfig> serviceConfig)
    {
        _serviceConfig = serviceConfig;
    }

    public override void Configure()
    {
        Post("/webhooks/{Id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(WebhookRequest req, CancellationToken ct)
    {
        if (!_serviceConfig.Value.Webhooks.Any(kvp => Equals(Guid.Parse(kvp.Key), req.Id)))
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await PublishAsync(req, Mode.WaitForNone, ct);
        await SendOkAsync(ct);
    }
}