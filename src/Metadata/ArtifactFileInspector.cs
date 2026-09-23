using System.Text;

namespace AppVeyorArtifactsReceiver.Metadata;

/// <summary>
///     Identifies stored artifacts that can produce a metadata sidecar.
/// </summary>
internal static class ArtifactFileInspector
{
    private static readonly byte[] CompoundFileSignature =
    [
        0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1
    ];

    private static readonly string[] PeLikeExtensions =
    [
        ".exe", ".dll", ".sys", ".ocx", ".scr", ".efi", ".cpl", ".mui", ".drv", ".msc"
    ];

    public static ArtifactMetadataKind Detect(string fileName, Stream stream)
    {
        bool compoundFile = HasCompoundFileSignature(stream);
        if (HasMsiExtension(fileName))
        {
            return compoundFile ? ArtifactMetadataKind.Msi : ArtifactMetadataKind.None;
        }

        if (compoundFile)
        {
            return ArtifactMetadataKind.Msi;
        }

        return IsPeFile(stream) ? ArtifactMetadataKind.Pe : ArtifactMetadataKind.None;
    }

    public static ArtifactMetadataKind ClassifyEntry(string entryName, ReadOnlySpan<byte> header)
    {
        if (HasMsiExtension(entryName))
        {
            return ArtifactMetadataKind.Msi;
        }

        if (HasPeLikeExtension(entryName))
        {
            return ArtifactMetadataKind.Pe;
        }

        if (IsMzHeader(header))
        {
            return ArtifactMetadataKind.Pe;
        }

        if (IsCompoundFileHeader(header))
        {
            return ArtifactMetadataKind.Msi;
        }

        return ArtifactMetadataKind.None;
    }

    public static bool HasMsiExtension(string fileName)
    {
        return Path.GetExtension(fileName).Equals(".msi", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasPeLikeExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);
        foreach (string candidate in PeLikeExtensions)
        {
            if (extension.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasCompoundFileSignature(Stream stream)
    {
        try
        {
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            byte[] buffer = new byte[CompoundFileSignature.Length];
            int read = stream.Read(buffer, 0, buffer.Length);
            if (stream.CanSeek)
            {
                stream.Seek(0, SeekOrigin.Begin);
            }

            return IsCompoundFileHeader(buffer.AsSpan(0, read));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsCompoundFileHeader(ReadOnlySpan<byte> header)
    {
        return header.Length >= CompoundFileSignature.Length &&
               header.StartsWith(CompoundFileSignature);
    }

    public static bool IsMzHeader(ReadOnlySpan<byte> header)
    {
        return header.Length >= 2 && header[0] == 0x4D && header[1] == 0x5A;
    }

    public static bool IsPeFile(Stream stream)
    {
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            if (reader.ReadUInt16() != 0x5A4D)
            {
                return false;
            }

            stream.Seek(0x3C, SeekOrigin.Begin);
            int peHeaderOffset = reader.ReadInt32();
            stream.Seek(peHeaderOffset, SeekOrigin.Begin);
            return reader.ReadUInt32() == 0x00004550;
        }
        catch
        {
            return false;
        }
    }
}
