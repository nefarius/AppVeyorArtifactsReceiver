namespace AppVeyorArtifactsReceiver.Metadata;

internal interface IMsiPropertyTableExporter
{
    Task<MsiPropertyTableExport> ExportAsync(string packagePath, CancellationToken cancellationToken);
}

internal readonly record struct MsiPropertyTableExport(bool Succeeded, string Content, string FailureDetail)
{
    public static MsiPropertyTableExport Success(string content)
    {
        return new MsiPropertyTableExport(true, content, null);
    }

    public static MsiPropertyTableExport Failed(string detail)
    {
        return new MsiPropertyTableExport(false, null, detail);
    }
}
