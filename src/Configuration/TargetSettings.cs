#nullable enable
using System.Diagnostics.CodeAnalysis;

namespace AppVeyorArtifactsReceiver.Configuration;

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
}