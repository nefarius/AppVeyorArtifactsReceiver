using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AppVeyorArtifactsReceiver.Models;

[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global")]
public sealed record MsiArtifactMetaData(
    [property: JsonPropertyName("type")] string Type,
    string ProductVersion,
    string ProductName,
    string Manufacturer,
    string ProductCode)
{
    public MsiArtifactMetaData(
        string productVersion,
        string productName,
        string manufacturer,
        string productCode)
        : this(ArtifactMetadataTypes.Msi, productVersion, productName, manufacturer, productCode)
    {
    }
}
