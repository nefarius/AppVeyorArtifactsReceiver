namespace AppVeyorArtifactsReceiver.Metadata;

internal sealed class MsiMetadataReader : IMsiMetadataReader
{
    private readonly IMsiMetadataReader _inner = OperatingSystem.IsWindows()
        ? new WindowsInstallerMsiMetadataReader()
        : new MsiInfoMsiMetadataReader(new MsiInfoProcess());

    public Task<MsiMetadataReadResult> ReadAsync(string packagePath, CancellationToken cancellationToken)
    {
        return _inner.ReadAsync(packagePath, cancellationToken);
    }
}
