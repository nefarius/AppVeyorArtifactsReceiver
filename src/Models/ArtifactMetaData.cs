using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AppVeyorArtifactsReceiver.Models;

[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global")]
public sealed record ArtifactMetaData(
    [property: JsonPropertyName("type")] string Type,
    string FileVersion,
    string ProductVersion)
{
    public ArtifactMetaData(string fileVersion, string productVersion)
        : this(ArtifactMetadataTypes.Pe, fileVersion, productVersion)
    {
    }
}
