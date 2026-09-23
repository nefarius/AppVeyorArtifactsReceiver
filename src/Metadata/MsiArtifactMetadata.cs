using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Metadata;

internal static class MsiArtifactMetadata
{
    public static MsiArtifactMetaData FromProperties(IReadOnlyDictionary<string, string> properties)
    {
        return new MsiArtifactMetaData(
            Value(properties, "ProductVersion"),
            Value(properties, "ProductName"),
            Value(properties, "Manufacturer"),
            Value(properties, "ProductCode"));
    }

    private static string Value(IReadOnlyDictionary<string, string> properties, string name)
    {
        if (!properties.TryGetValue(name, out string value) || string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value;
    }
}
