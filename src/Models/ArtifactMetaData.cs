using System.Diagnostics.CodeAnalysis;

namespace AppVeyorArtifactsReceiver.Models;

[SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Global")]
public sealed record ArtifactMetaData(string FileVersion, string ProductVersion);