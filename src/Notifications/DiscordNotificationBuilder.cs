#nullable enable
using System.Diagnostics.CodeAnalysis;
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

    public static DiscordWebhookPayload Build(
        WebhookRequest request,
        JobProcessingResult result,
        string? publicArtifactsBaseUrl = null)
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
                Field("Branch", FormatBranch(request), inline: true),
                Field("Commit", FormatCommit(request), inline: true),
                Field("Artifacts", $"{result.ArtifactsSucceeded} succeeded, {result.ArtifactsFailed} failed",
                    inline: true),
                Field("Target", FormatTarget(result.TargetSubDirectory, publicArtifactsBaseUrl), inline: true)
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
            GetEnv(request, "github_head_ref"),
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

    private static string FormatTarget(string? relativePath, string? publicArtifactsBaseUrl)
    {
        string label = DisplayOrUnknown(relativePath);
        return TryCreatePublicTargetUrl(publicArtifactsBaseUrl, relativePath, out string? url)
            ? MarkdownLink(label, url)
            : label;
    }

    private static string FormatBranch(WebhookRequest request)
    {
        string branch = ResolveBranch(request);
        return TryCreateGitHubUrl(request, "tree", branch, out string? url)
            ? MarkdownLink(branch, url)
            : branch;
    }

    private static string FormatCommit(WebhookRequest request)
    {
        string commit = ResolveCommit(request);
        string label = AbbreviateCommit(commit);
        if (!LooksLikeCommitSha(commit) ||
            !TryCreateGitHubUrl(request, "commit", commit, out string? url))
        {
            return label;
        }

        return MarkdownLink(label, url);
    }

    private static bool TryCreatePublicTargetUrl(
        string? publicArtifactsBaseUrl,
        string? relativePath,
        [NotNullWhen(true)] out string? url)
    {
        url = null;
        if (string.IsNullOrWhiteSpace(publicArtifactsBaseUrl) ||
            string.IsNullOrWhiteSpace(relativePath) ||
            !TryGetSafeRelativeSegments(relativePath, out string[] segments) ||
            !Uri.TryCreate(publicArtifactsBaseUrl.Trim(), UriKind.Absolute, out Uri? baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        try
        {
            var builder = new UriBuilder(baseUri)
            {
                Query = string.Empty,
                Fragment = string.Empty
            };
            string basePath = builder.Path.TrimEnd('/');
            if (basePath == "/")
            {
                basePath = string.Empty;
            }

            builder.Path = basePath + "/" + string.Join('/', segments.Select(Uri.EscapeDataString));
            url = builder.Uri.AbsoluteUri;
            return true;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool TryCreateGitHubUrl(
        WebhookRequest request,
        string kind,
        string resource,
        [NotNullWhen(true)] out string? url)
    {
        url = null;
        if (!TryResolveGitHubRepository(request, out string? owner, out string? repo) ||
            !TryGetSafeRelativeSegments(resource, out string[] segments))
        {
            return false;
        }

        url = $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{kind}/" +
              string.Join('/', segments.Select(Uri.EscapeDataString));
        return true;
    }

    private static bool TryResolveGitHubRepository(
        WebhookRequest request,
        [NotNullWhen(true)] out string? owner,
        [NotNullWhen(true)] out string? repo)
    {
        owner = null;
        repo = null;

        if (TryParseOwnerRepo(GetEnv(request, "github_repository"), out owner, out repo))
        {
            return true;
        }

        string? provider = GetEnv(request, "appveyor_repo_provider");
        if (!string.Equals(provider, "github", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseOwnerRepo(
            FirstNonEmpty(request.RepositoryName, GetEnv(request, "appveyor_repo_name")),
            out owner,
            out repo);
    }

    private static bool TryParseOwnerRepo(
        string? slug,
        [NotNullWhen(true)] out string? owner,
        [NotNullWhen(true)] out string? repo)
    {
        owner = null;
        repo = null;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return false;
        }

        string[] parts = slug.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            parts.Any(part => part is "." or ".." || part.IndexOfAny(['\\', ':']) >= 0))
        {
            return false;
        }

        owner = parts[0];
        repo = parts[1];
        return true;
    }

    private static bool TryGetSafeRelativeSegments(string value, out string[] segments)
    {
        string normalized = value.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(normalized) ||
            Path.IsPathRooted(normalized) ||
            normalized.Contains(':', StringComparison.Ordinal))
        {
            segments = [];
            return false;
        }

        segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && Array.TrueForAll(segments, segment => segment is not "." and not "..");
    }

    private static bool LooksLikeCommitSha(string commit)
    {
        return commit.Length >= 7 && commit.All(Uri.IsHexDigit);
    }

    private static string MarkdownLink(string label, string url)
    {
        if (label.IndexOfAny(['[', ']']) >= 0)
        {
            return label;
        }

        string link = $"[{label}]({url})";
        return link.Length > 1024 ? label : link;
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
