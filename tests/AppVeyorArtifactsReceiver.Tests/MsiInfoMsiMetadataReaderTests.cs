using AppVeyorArtifactsReceiver.Metadata;
using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class MsiInfoMsiMetadataReaderTests
{
    [Fact]
    public async Task Reads_msi_fields_and_leaves_missing_properties_null()
    {
        const string export =
            "Property\tValue\r\ns72\tl0\r\nProperty\tProperty\r\nProductName\tDsHidMini\r\nManufacturer\tNefarius\r\n";
        MsiInfoMsiMetadataReader reader = new(new StaticExporter(MsiPropertyTableExport.Success(export)));

        MsiMetadataReadResult result = await reader.ReadAsync("setup.msi", CancellationToken.None);

        Assert.True(result.Succeeded);
        MsiArtifactMetaData metadata = result.Metadata;
        Assert.Equal(ArtifactMetadataTypes.Msi, metadata.Type);
        Assert.Equal("DsHidMini", metadata.ProductName);
        Assert.Equal("Nefarius", metadata.Manufacturer);
        Assert.Null(metadata.ProductVersion);
        Assert.Null(metadata.ProductCode);
    }

    [Fact]
    public async Task Returns_exporter_failures_without_throwing()
    {
        MsiInfoMsiMetadataReader reader = new(new StaticExporter(
            MsiPropertyTableExport.Failed("msiinfo is not available: not found")));

        MsiMetadataReadResult result = await reader.ReadAsync("setup.msi", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Metadata);
        Assert.Equal("msiinfo is not available: not found", result.FailureDetail);
    }

    [Fact]
    public async Task Returns_malformed_exports_as_failures()
    {
        MsiInfoMsiMetadataReader reader = new(new StaticExporter(MsiPropertyTableExport.Success("not a table")));

        MsiMetadataReadResult result = await reader.ReadAsync("setup.msi", CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Null(result.Metadata);
        Assert.False(string.IsNullOrWhiteSpace(result.FailureDetail));
    }

    [Fact]
    public async Task Propagates_cancellation()
    {
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        MsiInfoMsiMetadataReader reader = new(new CancelingExporter());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync("setup.msi", cancellation.Token));
    }

    private sealed class StaticExporter(MsiPropertyTableExport export) : IMsiPropertyTableExporter
    {
        public Task<MsiPropertyTableExport> ExportAsync(string packagePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(export);
        }
    }

    private sealed class CancelingExporter : IMsiPropertyTableExporter
    {
        public Task<MsiPropertyTableExport> ExportAsync(string packagePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(MsiPropertyTableExport.Failed("should have been canceled"));
        }
    }
}
