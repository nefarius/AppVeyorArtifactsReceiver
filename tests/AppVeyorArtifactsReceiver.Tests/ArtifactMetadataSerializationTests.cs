using System.Text.Json;

using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class ArtifactMetadataSerializationTests
{
    [Fact]
    public void Pe_sidecar_includes_type_discriminator()
    {
        string json = JsonSerializer.Serialize(new ArtifactMetaData("1.2.3.4", "9.8.7.6"));

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(ArtifactMetadataTypes.Pe, document.RootElement.GetProperty("type").GetString());
        Assert.Equal("1.2.3.4", document.RootElement.GetProperty("FileVersion").GetString());
        Assert.Equal("9.8.7.6", document.RootElement.GetProperty("ProductVersion").GetString());
        Assert.Equal(3, document.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public void Msi_sidecar_includes_type_and_preserves_missing_fields()
    {
        MsiArtifactMetaData metadata = new(
            productVersion: null,
            productName: "DsHidMini",
            manufacturer: null,
            productCode: "{23170F69-40C1-2702-0000-000004000000}");

        string json = JsonSerializer.Serialize(metadata);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(ArtifactMetadataTypes.Msi, document.RootElement.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("ProductVersion").ValueKind);
        Assert.Equal("DsHidMini", document.RootElement.GetProperty("ProductName").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("Manufacturer").ValueKind);
        Assert.Equal(
            "{23170F69-40C1-2702-0000-000004000000}",
            document.RootElement.GetProperty("ProductCode").GetString());
    }
}
