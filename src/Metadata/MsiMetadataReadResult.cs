using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Metadata;

internal readonly record struct MsiMetadataReadResult(MsiArtifactMetaData Metadata, string FailureDetail)
{
    public bool Succeeded => Metadata != null;

    public static MsiMetadataReadResult Success(MsiArtifactMetaData metadata)
    {
        return new MsiMetadataReadResult(metadata, null);
    }

    public static MsiMetadataReadResult Failed(string detail)
    {
        return new MsiMetadataReadResult(null, detail);
    }
}
