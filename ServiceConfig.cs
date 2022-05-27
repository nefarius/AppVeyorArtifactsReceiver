namespace AppVeyorArtifactsReceiver;

public class TargetSettings
{
    /// <summary>
    ///     The sub-directory to put the artifact in. Supports all AppVeyor environment variables as substitutes for
    ///     placeholders.
    /// </summary>
    public string TargetPathTemplate { get; set; }

    /// <summary>
    ///     The download/wwwroot directory.
    /// </summary>
    public string RootDirectory { get; set; }
}

public class ServiceConfig
{
    /// <summary>
    ///     The configured accepted webhooks.
    /// </summary>
    public Dictionary<string, TargetSettings> Webhooks { get; set; } = new();
}