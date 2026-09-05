#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace AppVeyorArtifactsReceiver.Configuration;

/// <summary>
///     Represents the settings used for configuring a target folder for storing artifacts
///     received from webhook requests. It includes templates for file paths and options
///     for maintaining metadata related to the stored artifacts.
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class TargetSettings
{
    /// <summary>
    ///     The subdirectory to put the artifact in. Supports <c>{placeholder}</c> tokens from the webhook
    ///     <c>environmentVariables</c> object (AppVeyor deployment variables or GitHub values synthesized by the bundled
    ///     action).
    /// </summary>
    public required string TargetPathTemplate { get; set; }

    /// <summary>
    ///     The subdirectory to put the symbolic link to the latest build in. Supports the same <c>{placeholder}</c> tokens as
    ///     <see cref="TargetPathTemplate" />.
    /// </summary>
    public string? LatestSymlinkTemplate { get; set; }

    /// <summary>
    ///     The download/wwwroot directory.
    /// </summary>
    public required string RootDirectory { get; set; }

    /// <summary>
    ///     Gets whether automatic ".artifact-name.exe.json" metadata files should be generated.
    /// </summary>
    public bool StoreMetaData { get; set; } = true;

    /// <summary>
    ///     Maximum ZIP entries examined per artifact (GitHub Actions extraction and AppVeyor PE metadata).
    ///     Zero uses the built-in default (8192).
    /// </summary>
    public int ZipMaxEntriesToScan { get; set; }

    /// <summary>
    ///     Maximum uncompressed size in bytes of a single ZIP entry to extract or load for PE parsing.
    ///     Zero uses the built-in default (256 MiB).
    /// </summary>
    public long ZipMaxEntryBytes { get; set; }
}