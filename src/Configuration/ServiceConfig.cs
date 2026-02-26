#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace AppVeyorArtifactsReceiver.Configuration;

/// <summary>
///     Represents the configuration settings for the service, including webhook targets.
/// </summary>
/// <remarks>
///     This class is designed to hold service-specific configuration details, facilitating
///     streamlined integration with external webhook handlers or processors. It provides
///     a centralized structure where different webhook targets and their associated
///     configurations can be managed.
/// </remarks>
[SuppressMessage("ReSharper", "CollectionNeverUpdated.Global")]
public sealed class ServiceConfig
{
    /// <summary>
    ///     The configured accepted webhooks.
    /// </summary>
    public Dictionary<string /* Webhook GUID */, TargetSettings> Webhooks { get; init; } = new();
}