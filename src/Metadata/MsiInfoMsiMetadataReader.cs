namespace AppVeyorArtifactsReceiver.Metadata;

internal sealed class MsiInfoMsiMetadataReader(IMsiPropertyTableExporter exporter) : IMsiMetadataReader
{
    public async Task<MsiMetadataReadResult> ReadAsync(string packagePath, CancellationToken cancellationToken)
    {
        MsiPropertyTableExport export = await exporter.ExportAsync(packagePath, cancellationToken);
        if (!export.Succeeded)
        {
            return MsiMetadataReadResult.Failed(export.FailureDetail ?? "msiinfo failed.");
        }

        if (!MsiPropertyTableParser.TryParse(export.Content, out Dictionary<string, string> properties, out string error))
        {
            return MsiMetadataReadResult.Failed(error);
        }

        return MsiMetadataReadResult.Success(MsiArtifactMetadata.FromProperties(properties));
    }
}
