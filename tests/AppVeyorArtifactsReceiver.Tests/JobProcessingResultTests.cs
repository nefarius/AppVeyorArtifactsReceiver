using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class JobProcessingResultTests
{
    [Fact]
    public void Outcome_is_success_when_no_errors_are_recorded()
    {
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();
        result.RecordArtifactSuccess();

        Assert.Equal(JobOutcome.Success, result.Outcome);
        Assert.Equal(2, result.ArtifactsSucceeded);
        Assert.Equal(0, result.ArtifactsFailed);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Outcome_is_partial_failure_when_some_artifacts_succeed_and_some_fail()
    {
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();
        result.RecordArtifactFailure("Failed to copy a.dll to disk");

        Assert.Equal(JobOutcome.PartialFailure, result.Outcome);
        Assert.Equal(1, result.ArtifactsSucceeded);
        Assert.Equal(1, result.ArtifactsFailed);
        Assert.Equal(["Failed to copy a.dll to disk"], result.Errors);
    }

    [Fact]
    public void Outcome_is_failure_when_every_artifact_fails()
    {
        JobProcessingResult result = new();
        result.RecordArtifactFailure("first");
        result.RecordArtifactFailure("second");

        Assert.Equal(JobOutcome.Failure, result.Outcome);
        Assert.Equal(0, result.ArtifactsSucceeded);
        Assert.Equal(2, result.ArtifactsFailed);
    }

    [Fact]
    public void Outcome_is_failure_for_empty_artifact_sets_and_unsafe_paths()
    {
        JobProcessingResult empty = new();
        empty.RecordError("No artifacts found for build 42");

        JobProcessingResult unsafePath = new();
        unsafePath.RecordError("Expanded target path ..\\escape is rooted or escapes RootDirectory /data");

        Assert.Equal(JobOutcome.Failure, empty.Outcome);
        Assert.Equal(JobOutcome.Failure, unsafePath.Outcome);
    }

    [Fact]
    public void Outcome_is_failure_when_artifacts_succeed_but_post_processing_fails()
    {
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();
        result.RecordError("Failed to create symbolic link: Access denied");

        Assert.Equal(JobOutcome.Failure, result.Outcome);
        Assert.Equal(1, result.ArtifactsSucceeded);
        Assert.Equal(0, result.ArtifactsFailed);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void RecordError_ignores_blank_messages()
    {
        JobProcessingResult result = new();
        result.RecordError("   ");
        result.RecordError(string.Empty);

        Assert.Equal(JobOutcome.Success, result.Outcome);
        Assert.Empty(result.Errors);
    }
}
