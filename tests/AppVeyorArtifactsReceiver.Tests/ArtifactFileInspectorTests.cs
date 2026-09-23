using AppVeyorArtifactsReceiver.Metadata;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class ArtifactFileInspectorTests
{
    [Fact]
    public void Detects_pe_files_and_msi_compound_files()
    {
        using MemoryStream pe = new(MinimalPe());
        using MemoryStream msi = new(CompoundFile());
        using MemoryStream mislabeledMsi = new(MinimalPe());
        using MemoryStream text = new("hello"u8.ToArray());

        Assert.Equal(ArtifactMetadataKind.Pe, ArtifactFileInspector.Detect("App.exe", pe));
        Assert.Equal(ArtifactMetadataKind.Msi, ArtifactFileInspector.Detect("Setup.msi", msi));
        Assert.Equal(ArtifactMetadataKind.Msi, ArtifactFileInspector.Detect("payload.bin", msi));
        Assert.Equal(ArtifactMetadataKind.None, ArtifactFileInspector.Detect("Setup.msi", mislabeledMsi));
        Assert.Equal(ArtifactMetadataKind.None, ArtifactFileInspector.Detect("readme.txt", text));
    }

    [Theory]
    [InlineData("bin/Setup.msi", "msi")]
    [InlineData("App.DLL", "pe")]
    [InlineData("driver.sys", "pe")]
    public void Classifies_known_extensions_without_a_header(string entryName, string expected)
    {
        ArtifactMetadataKind kind = expected switch
        {
            "msi" => ArtifactMetadataKind.Msi,
            "pe" => ArtifactMetadataKind.Pe,
            _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, "Unknown metadata kind.")
        };

        Assert.Equal(kind, ArtifactFileInspector.ClassifyEntry(entryName, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Classifies_extensionless_entries_from_the_header()
    {
        byte[] compound = CompoundFile();
        byte[] mz = [0x4D, 0x5A, 0x00, 0x00];

        Assert.Equal(ArtifactMetadataKind.Msi, ArtifactFileInspector.ClassifyEntry("payload.bin", compound));
        Assert.Equal(ArtifactMetadataKind.Pe, ArtifactFileInspector.ClassifyEntry("payload.bin", mz));
        Assert.Equal(ArtifactMetadataKind.None, ArtifactFileInspector.ClassifyEntry("payload.bin", "text"u8));
    }

    private static byte[] MinimalPe()
    {
        byte[] bytes = new byte[0x44];
        bytes[0] = 0x4D;
        bytes[1] = 0x5A;
        bytes[0x3C] = 0x40;
        bytes[0x40] = 0x50;
        bytes[0x41] = 0x45;
        return bytes;
    }

    private static byte[] CompoundFile()
    {
        return [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    }
}
