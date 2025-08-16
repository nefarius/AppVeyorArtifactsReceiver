﻿using System.Diagnostics.CodeAnalysis;
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
///     Service that processes received artifacts and stores them on disk.
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

                    await using FileStream file = File.Create(absolutePath);

                    await stream.CopyToAsync(file, ct);

                    if (hookCfg.StoreMetaData && IsPEFile(file))
                    {
                        await ExtractPEMetaData(file, absolutePath, ct);
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

                    // create/update a file with the last updated timestamp in it for other APIs (or users) to use 
                    string timestampFileAbsolutePath = Path.Combine(absoluteTargetPath, "LAST_UPDATED_AT.txt");
                    await using StreamWriter tsFile = File.CreateText(timestampFileAbsolutePath);
                    await tsFile.WriteAsync(DateTime.UtcNow.ToString("O"));
                    tsFile.Close();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create symbolic link");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process webhook request");
        }
    }

    private async Task ExtractPEMetaData(FileStream file, string absolutePath, CancellationToken ct = default)
    {
        try
        {
            // attempt to put PE metadata into a separate JSON file for automated use
            file.Position = 0;
            PeFile peFile = new(file);

            if (peFile.Resources?.VsVersionInfo != null)
            {
                StringTable stringTable =
                    peFile.Resources.VsVersionInfo!.StringFileInfo.StringTable.First();

                string metaDirectory = Path.GetDirectoryName(absolutePath);
                string metaFileName = Path.GetFileName(absolutePath);

                string metaAbsolutePath = Path.Combine(metaDirectory!, $".{metaFileName}.json");
                ArtifactMetaData meta = new(stringTable.FileVersion, stringTable.ProductVersion);

                await File.WriteAllTextAsync(metaAbsolutePath, JsonSerializer.Serialize(meta), ct);

                logger.LogInformation("Generated meta-data file {MetaFile}", metaAbsolutePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to PE-parse file {File}", absolutePath);
        }
    }

    private static bool IsPEFile(FileStream stream)
    {
        try
        {
            stream.Seek(0, SeekOrigin.Begin);
            using BinaryReader reader = new(stream, Encoding.UTF8, true);
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