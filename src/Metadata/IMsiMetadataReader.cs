namespace AppVeyorArtifactsReceiver.Metadata;

internal interface IMsiMetadataReader
{
    Task<MsiMetadataReadResult> ReadAsync(string packagePath, CancellationToken cancellationToken);
}
