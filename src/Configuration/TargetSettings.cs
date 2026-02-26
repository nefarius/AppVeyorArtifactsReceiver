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
    ///     The subdirectory to put the artifact in. Supports all AppVeyor environment variables as substitutes for
    ///     placeholders.
    /// </summary>
    public required string TargetPathTemplate { get; set; }

    /// <summary>
    ///     The subdirectory to put the symbolic link to the latest build in. Supports all AppVeyor environment variables as
    ///     substitutes for placeholders.
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
}