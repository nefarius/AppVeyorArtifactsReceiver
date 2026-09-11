#nullable enable
using System.Text;
using System.Text.Json;

using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Notifications;

/// <summary>
///     Builds a Discord incoming-webhook payload from a processed job.
/// </summary>
internal static class DiscordNotificationBuilder
{
    public const int SuccessColor = 0x57F287;
    public const int FailureColor = 0xED4245;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null
    };

    public static DiscordWebhookPayload Build(WebhookRequest request, JobProcessingResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        bool success = result.Outcome == JobOutcome.Success;
        string title = result.Outcome switch
        {
            JobOutcome.Success => "Artifacts received",
            JobOutcome.PartialFailure => "Artifact processing partially failed",
            _ => "Artifact processing failed"
        };

        var embed = new DiscordEmbed
        {
            Title = title,
            Description = Truncate(FirstNonEmpty(request.CommitMessage, request.CommitMessageExtended), 200),
            Color = success ? SuccessColor : FailureColor,
            Fields =
            [
                Field("Project", ResolveProject(request), inline: true),
                Field("Build", ResolveBuild(request), inline: true),
                Field("Branch", ResolveBranch(request), inline: true),
                Field("Commit", AbbreviateCommit(ResolveCommit(request)), inline: true),
                Field("Artifacts", $"{result.ArtifactsSucceeded} succeeded, {result.ArtifactsFailed} failed",
                    inline: true),
                Field("Target", DisplayOrUnknown(result.TargetSubDirectory), inline: true)
            ]
        };

        if (result.Errors.Count > 0)
        {
            embed.Fields.Add(Field("Errors", FormatErrors(result.Errors), inline: false));
        }

        return new DiscordWebhookPayload
        {
            Embeds = [embed]
        };
    }

    public static string Serialize(DiscordWebhookPayload payload)
    {
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static DiscordEmbedField Field(string name, string value, bool inline)
    {
        return new DiscordEmbedField
        {
            Name = name,
            Value = string.IsNullOrWhiteSpace(value) ? "unknown" : Truncate(value, 1024)!,
            Inline = inline
        };
    }

    private static string ResolveProject(WebhookRequest request)
    {
        return FirstNonEmpty(
            request.ProjectName,
            request.RepositoryName,
            GetEnv(request, "github_repository"),
            GetEnv(request, "github_repository_name"),
            GetEnv(request, "appveyor_project_name")) ?? "unknown";
    }

    private static string ResolveBuild(WebhookRequest request)
    {
        string? buildNumber = request.BuildNumber > 0 ? request.BuildNumber.ToString() : null;
        return FirstNonEmpty(
            request.BuildVersion,
            buildNumber,
            GetEnv(request, "github_run_number"),
            GetEnv(request, "appveyor_build_version")) ?? "unknown";
    }

    private static string ResolveBranch(WebhookRequest request)
    {
        return FirstNonEmpty(
            request.Branch,
            GetEnv(request, "github_ref_name"),
            GetEnv(request, "appveyor_repo_branch")) ?? "unknown";
    }

    private static string ResolveCommit(WebhookRequest request)
    {
        return FirstNonEmpty(
            request.CommitId,
            GetEnv(request, "github_sha"),
            GetEnv(request, "appveyor_repo_commit")) ?? "unknown";
    }

    private static string? GetEnv(WebhookRequest request, string key)
    {
        if (request.EnvironmentVariables is null)
        {
            return null;
        }

        return request.EnvironmentVariables.TryGetValue(key, out string? value) ? value : null;
    }

    private static string AbbreviateCommit(string commit)
    {
        if (commit.Length >= 7 && commit.All(c => Uri.IsHexDigit(c)))
        {
            return commit[..7];
        }

        return Truncate(commit, 32) ?? commit;
    }

    private static string FormatErrors(IReadOnlyList<string> errors)
    {
        const int maxLength = 1000;
        var builder = new StringBuilder();
        int shown = 0;

        foreach (string error in errors)
        {
            string line = $"- {error}";
            if (builder.Length + line.Length + 1 > maxLength)
            {
                builder.Append("\n- …");
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(line);
            shown++;
        }

        if (shown < errors.Count && !builder.ToString().EndsWith('…'))
        {
            builder.Append("\n- …");
        }

        return builder.Length == 0 ? "unknown" : builder.ToString();
    }

    private static string DisplayOrUnknown(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..(maxLength - 1)] + "…";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
