#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace AppVeyorArtifactsReceiver.Configuration;

[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
public sealed class ServiceConfig
{
    /// <summary>
    ///     The configured accepted webhooks.
    /// </summary>
    public Dictionary<string /* Webhook GUID */, TargetSettings> Webhooks { get; init; } = new();
}