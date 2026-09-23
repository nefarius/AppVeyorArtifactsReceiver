namespace AppVeyorArtifactsReceiver.Models;

/// <summary>
///     Stable sidecar discriminators. Consumers should branch on <c>type</c> and ignore values they do not know.
/// </summary>
public static class ArtifactMetadataTypes
{
    public const string Pe = "pe";

    public const string Msi = "msi";
}
