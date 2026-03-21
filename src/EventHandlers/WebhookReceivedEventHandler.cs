using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;

using JetBrains.Annotations;

using Microsoft.Extensions.Options;

using PeNet;
using PeNet.Header.Resource;

namespace AppVeyorArtifactsReceiver.EventHandlers;

/// <summary>
///     Handles incoming webhook events by processing artifact information contained in the request.
///     Extracts and validates data, retrieves configuration settings, and ensures that artifacts are
///     stored in the specified target destinations. Handles scenarios such as missing artifacts,
///     updates to target directories, and other processing-specific outcomes.
/// </summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
[UsedImplicitly]
internal sealed partial class WebhookReceivedEventHandler(
    ILogger<WebhookReceivedEventHandler> logger,
    IOptionsSnapshot<ServiceConfig> serviceConfig,
    IHttpClientFactory httpClientFactory)
    : IEventHandler<WebhookRequest>
{
    public async Task HandleAsync(WebhookRequest req, CancellationToken ct)
    {
        TargetSettings hookCfg = serviceConfig.Value.Webhooks
            .First(kvp => Equals(Guid.Parse(kvp.Key), req.Id)).Value;

        logger.LogDebug("Target settings: {@TargetSettings}", hookCfg);
        logger.LogDebug("Request: {@WebhookRequest}", req);

        try
        {
            string subDirectory = Replace(hookCfg.TargetPathTemplate, req.EnvironmentVariables);

            logger.LogInformation("Build sub-directory {Directory}", subDirectory);

            string absoluteTargetPath = Path.Combine(hookCfg.RootDirectory, subDirectory);
            Directory.CreateDirectory(absoluteTargetPath);

            if (req.Artifacts.Count == 0)
            {
                logger.LogWarning("No artifacts found for build {BuildId}", req.BuildId);
                return;
            }

            // each job can have multiple artifacts
            foreach (Artifact artifact in req.Artifacts)
            {
                string absolutePath = Path.Combine(hookCfg.RootDirectory, subDirectory, artifact.FileName);

                try
                {
                    logger.LogInformation("Sub-path for artifact {FileName}: {Path}",
                        artifact.FileName, subDirectory);

                    Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

                    using HttpClient httpClient = httpClientFactory.CreateClient("AppVeyor");

                    // GitHub artifacts accessed via REST API require token auth
                    if (!string.IsNullOrEmpty(req.GitHubToken))
                    {
                        httpClient.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", req.GitHubToken);
                    }

                    await using Stream stream = await httpClient.GetStreamAsync(artifact.Url, ct);

                    await using (FileStream file = File.Create(absolutePath))
                    {
                        await stream.CopyToAsync(file, ct);
                    }

                    if (hookCfg.StoreMetaData)
                    {
                        await using FileStream readStream = File.OpenRead(absolutePath);
                        if (IsZipFile(readStream, artifact.FileName))
                        {
                            await ExtractZipPeMetadata(absolutePath, ct);
                        }
                        else if (IsPEFile(readStream))
                        {
                            await WritePeMetadataSidecarForFile(readStream, absolutePath, ct);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to copy {File} to disk", absolutePath);
                }
            }

            // updates the "latest" special directory symlink with the most up-to-date target
            if (!string.IsNullOrEmpty(hookCfg.TargetPathTemplate))
            {
                string latestSubDirectory = Replace(hookCfg.LatestSymlinkTemplate, req.EnvironmentVariables);
                string absoluteSymlinkPath = Path.Combine(hookCfg.RootDirectory, latestSubDirectory);

                try
                {
                    if (Directory.Exists(absoluteSymlinkPath))
                    {
                        Directory.Delete(absoluteSymlinkPath);
                    }

                    FileSystemInfo linkInfo = File.CreateSymbolicLink(absoluteSymlinkPath, absoluteTargetPath);
                    logger.LogInformation("Created/updated symbolic link {Link}", linkInfo);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create symbolic link");
                }

                try
                {
                    await CreateTimestampFile(absoluteTargetPath);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create timestamp file");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process webhook request");
        }
    }

    private async Task CreateTimestampFile(string absoluteTargetPath)
    {
        DateTime timestamp = DateTime.UtcNow;
        string timestampString = timestamp.ToString("O");

        // create/update a file with the last updated timestamp in it for other APIs (or users) to use
        string timestampFileAbsolutePath = Path.Combine(absoluteTargetPath, "LAST_UPDATED_AT.txt");
        await using (StreamWriter tsFile = File.CreateText(timestampFileAbsolutePath))
        {
            await tsFile.WriteAsync(timestampString);
        }

        try
        {
            await CreateTimestampSvg(absoluteTargetPath, timestamp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create timestamp SVG");
        }
    }

    private static async Task CreateTimestampSvg(string absoluteTargetPath, DateTime timestamp)
    {
        const string label = "Last updated";
        string value = timestamp.ToString("dd MMM yyyy, HH:mm 'UTC'", CultureInfo.InvariantCulture);

        // Approximate character width for 11px monospace; add padding
        int labelWidth = label.Length * 7 + 20;
        int valueWidth = value.Length * 7 + 20;
        int totalWidth = labelWidth + valueWidth;
        const int height = 20;

        string escapedValue = value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        string svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{height}">
              <rect width="{labelWidth}" height="{height}" fill="#555"/>
              <rect x="{labelWidth}" width="{valueWidth}" height="{height}" fill="#4c1"/>
              <text x="{labelWidth / 2}" y="14" fill="#fff" text-anchor="middle" font-family="DejaVu Sans,Verdana,sans-serif" font-size="11">{label}</text>
              <text x="{labelWidth + valueWidth / 2}" y="14" fill="#fff" text-anchor="middle" font-family="DejaVu Sans Mono,monospace" font-size="11">{escapedValue}</text>
            </svg>
            """;

        string svgPath = Path.Combine(absoluteTargetPath, "LAST_UPDATED_AT.svg");
        await File.WriteAllTextAsync(svgPath, svg.Trim());
    }

    private async Task WritePeMetadataSidecarForFile(Stream file, string absolutePePath, CancellationToken ct = default)
    {
        string metaDirectory = Path.GetDirectoryName(absolutePePath)!;
        string metaFileName = Path.GetFileName(absolutePePath);
        string metaAbsolutePath = Path.Combine(metaDirectory, $".{metaFileName}.json");
        await TryWritePeMetadataJsonAsync(file, metaAbsolutePath, absolutePePath, ct);
    }

    private async Task TryWritePeMetadataJsonAsync(
        Stream stream,
        string metaAbsolutePath,
        string logSourcePath,
        CancellationToken ct = default)
    {
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            PeFile peFile = new(stream);

            if (peFile.Resources?.VsVersionInfo == null)
            {
                return;
            }

            StringTable stringTable =
                peFile.Resources.VsVersionInfo!.StringFileInfo.StringTable.First();

            ArtifactMetaData meta = new(stringTable.FileVersion, stringTable.ProductVersion);

            string metaDirectory = Path.GetDirectoryName(metaAbsolutePath);
            if (!string.IsNullOrEmpty(metaDirectory))
            {
                Directory.CreateDirectory(metaDirectory);
            }

            await File.WriteAllTextAsync(metaAbsolutePath, JsonSerializer.Serialize(meta), ct);

            logger.LogInformation("Generated meta-data file {MetaFile}", metaAbsolutePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to PE-parse file {File}", logSourcePath);
        }
    }

    private async Task ExtractZipPeMetadata(string zipAbsolutePath, CancellationToken ct)
    {
        string zipDirectory = Path.GetDirectoryName(zipAbsolutePath)!;
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir);
            ZipFile.ExtractToDirectory(zipAbsolutePath, tempDir);

            foreach (string extractedPath in Directory.EnumerateFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(tempDir, extractedPath);
                if (relative.Contains("..", StringComparison.Ordinal))
                {
                    logger.LogWarning("Skipping ZIP entry with invalid relative path {Path}", relative);
                    continue;
                }

                string metaAbsolutePath = BuildHiddenMetaJsonPathForZipEntry(zipDirectory, relative);
                if (string.IsNullOrEmpty(metaAbsolutePath))
                {
                    continue;
                }

                await using FileStream entryStream = File.OpenRead(extractedPath);
                if (!IsPEFile(entryStream))
                {
                    continue;
                }

                await TryWritePeMetadataJsonAsync(entryStream, metaAbsolutePath, extractedPath, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract or scan ZIP for PE metadata {Zip}", zipAbsolutePath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp directory {TempDir}", tempDir);
            }
        }
    }

    private static string BuildHiddenMetaJsonPathForZipEntry(string zipDirectory, string relativePathFromArchiveRoot)
    {
        string normalized = relativePathFromArchiveRoot.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string[] rawSegments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (rawSegments.Length == 0)
        {
            return string.Empty;
        }

        var segments = new List<string>(rawSegments.Length);
        foreach (string segment in rawSegments)
        {
            switch (segment)
            {
                case ".":
                    continue;
                case "..":
                    return string.Empty;
                default:
                    segments.Add(segment);
                    break;
            }
        }

        if (segments.Count == 0)
        {
            return string.Empty;
        }

        string fileName = segments[^1];
        string[] dirSegments = segments.Take(segments.Count - 1).Select(ToHiddenDirectorySegment).ToArray();
        string metaDir = dirSegments.Length == 0
            ? zipDirectory
            : Path.Combine(new[] { zipDirectory }.Concat(dirSegments).ToArray());

        return Path.Combine(metaDir, $".{fileName}.json");
    }

    private static string ToHiddenDirectorySegment(string segment) =>
        segment.Length > 0 && segment[0] == '.' ? segment : $".{segment}";

    private static bool IsZipFile(FileStream stream, string artifactFileName)
    {
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            uint signature = reader.ReadUInt32();
            if (signature == 0x04034b50)
            {
                return true;
            }

            if (Path.GetExtension(artifactFileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                stream.Seek(0, SeekOrigin.Begin);
                return reader.ReadUInt16() != 0x5A4D;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPEFile(Stream stream)
    {
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
            // Check for the "MZ" magic number at the start of the file
            if (reader.ReadUInt16() != 0x5A4D) // "MZ" in hex
            {
                return false;
            }

            // Move to the PE header offset location
            stream.Seek(0x3C, SeekOrigin.Begin);
            int peHeaderOffset = reader.ReadInt32();

            // Move to the PE header and check for the "PE\0\0" signature
            stream.Seek(peHeaderOffset, SeekOrigin.Begin);
            return reader.ReadUInt32() == 0x00004550; // "PE\0\0" in hex
        }
        catch
        {
            return false;
        }
    }

    [GeneratedRegex("{(?<placeholder>[a-z_][a-z0-9_]*?)}", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PlaceholdersRegex();

    /// <summary>
    ///     Replaces placeholder tokens (like {appveyor_project_name}) with the variable content of the payload.
    /// </summary>
    private static string Replace(string input, Dictionary<string, string> replacement)
    {
        return PlaceholdersRegex().Replace(input, m =>
        {
            string key = m.Groups["placeholder"].Value;
            return replacement.TryGetValue(key, out string value)
                ? value
                : throw new Exception($"Unknown key {key}");
        });
    }
}