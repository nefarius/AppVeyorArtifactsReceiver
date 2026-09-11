#nullable enable
namespace AppVeyorArtifactsReceiver.Models;

/// <summary>
///     Outcome of one webhook job after artifact processing (and optional post-processing) finishes.
/// </summary>
internal enum JobOutcome
{
    Success,
    PartialFailure,
    Failure
}

/// <summary>
///     Accumulates error-level outcomes for a single incoming webhook job.
///     Warning-only ZIP/PE skips are intentionally not recorded here.
/// </summary>
internal sealed class JobProcessingResult
{
    private readonly List<string> _errors = [];

    /// <summary>
    ///     Expanded target subdirectory when path templating succeeded.
    /// </summary>
    public string? TargetSubDirectory { get; set; }

    public int ArtifactsSucceeded { get; private set; }

    public int ArtifactsFailed { get; private set; }

    public IReadOnlyList<string> Errors => _errors;

    public JobOutcome Outcome
    {
        get
        {
            if (_errors.Count == 0)
            {
                return JobOutcome.Success;
            }

            if (ArtifactsSucceeded > 0 && ArtifactsFailed > 0)
            {
                return JobOutcome.PartialFailure;
            }

            return JobOutcome.Failure;
        }
    }

    public void RecordArtifactSuccess()
    {
        ArtifactsSucceeded++;
    }

    public void RecordArtifactFailure(string message)
    {
        ArtifactsFailed++;
        RecordError(message);
    }

    public void RecordError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _errors.Add(message.Trim());
    }
}
