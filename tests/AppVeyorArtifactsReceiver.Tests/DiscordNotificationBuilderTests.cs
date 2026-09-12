using System.Text.Json;

using AppVeyorArtifactsReceiver.Models;
using AppVeyorArtifactsReceiver.Notifications;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class DiscordNotificationBuilderTests
{
    [Fact]
    public void Success_payload_is_green_and_omits_errors()
    {
        WebhookRequest request = CreateAppVeyorRequest();
        JobProcessingResult result = new()
        {
            TargetSubDirectory = "builds/DsHidMini/master/1.2.3"
        };
        result.RecordArtifactSuccess();
        result.RecordArtifactSuccess();

        DiscordWebhookPayload payload = DiscordNotificationBuilder.Build(request, result);
        DiscordEmbed embed = Assert.Single(payload.Embeds);

        Assert.Empty(payload.AllowedMentions.Parse);
        Assert.Equal("Artifacts received", embed.Title);
        Assert.Equal(DiscordNotificationBuilder.SuccessColor, embed.Color);
        Assert.Equal("Ship it", embed.Description);
        Assert.Equal("DsHidMini", Field(embed, "Project"));
        Assert.Equal("1.2.3", Field(embed, "Build"));
        Assert.Equal("master", Field(embed, "Branch"));
        Assert.Equal("abcdef0", Field(embed, "Commit"));
        Assert.Equal("2 succeeded, 0 failed", Field(embed, "Artifacts"));
        Assert.Equal("builds/DsHidMini/master/1.2.3", Field(embed, "Target"));
        Assert.DoesNotContain(embed.Fields, field => field.Name == "Errors");
    }

    [Fact]
    public void Partial_failure_payload_is_red_and_includes_errors()
    {
        WebhookRequest request = CreateAppVeyorRequest();
        JobProcessingResult result = new()
        {
            TargetSubDirectory = "builds/DsHidMini/master/1.2.3"
        };
        result.RecordArtifactSuccess();
        result.RecordArtifactFailure("Failed to copy driver.sys to disk: 404");

        DiscordWebhookPayload payload = DiscordNotificationBuilder.Build(request, result);
        DiscordEmbed embed = Assert.Single(payload.Embeds);

        Assert.Equal("Artifact processing partially failed", embed.Title);
        Assert.Equal(DiscordNotificationBuilder.FailureColor, embed.Color);
        Assert.Equal("1 succeeded, 1 failed", Field(embed, "Artifacts"));
        Assert.Contains("Failed to copy driver.sys to disk: 404", Field(embed, "Errors"));
    }

    [Fact]
    public void Payload_falls_back_to_github_environment_variables()
    {
        WebhookRequest request = new()
        {
            Artifacts = [],
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["github_repository"] = "nefarius/DsHidMini",
                ["github_ref_name"] = "feat/discord",
                ["github_run_number"] = "88",
                ["github_sha"] = "0123456789abcdef0123456789abcdef01234567"
            }
        };
        JobProcessingResult result = new();
        result.RecordError("No artifacts found for build 0");

        DiscordEmbed embed = Assert.Single(DiscordNotificationBuilder.Build(request, result).Embeds);

        Assert.Equal("Artifact processing failed", embed.Title);
        Assert.Equal("nefarius/DsHidMini", Field(embed, "Project"));
        Assert.Equal("88", Field(embed, "Build"));
        Assert.Equal("[feat/discord](https://github.com/nefarius/DsHidMini/tree/feat/discord)",
            Field(embed, "Branch"));
        Assert.Equal(
            "[0123456](https://github.com/nefarius/DsHidMini/commit/0123456789abcdef0123456789abcdef01234567)",
            Field(embed, "Commit"));
    }

    [Fact]
    public void Target_is_linked_when_public_base_url_is_configured()
    {
        DiscordEmbed embed = EmbedWithTarget(
            "builds/DsHidMini/master/1.2.3",
            "https://artifacts.example.com/");

        Assert.Equal(
            "[builds/DsHidMini/master/1.2.3](https://artifacts.example.com/builds/DsHidMini/master/1.2.3)",
            Field(embed, "Target"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("ftp://artifacts.example.com")]
    [InlineData("javascript:alert(1)")]
    public void Target_stays_plain_text_when_public_base_url_is_missing_or_invalid(string? baseUrl)
    {
        DiscordEmbed embed = EmbedWithTarget("builds/DsHidMini/master/1.2.3", baseUrl);

        Assert.Equal("builds/DsHidMini/master/1.2.3", Field(embed, "Target"));
    }

    [Fact]
    public void Target_link_encodes_unsafe_path_segments()
    {
        DiscordEmbed embed = EmbedWithTarget(
            "builds/My App/release 1.0",
            "https://artifacts.example.com");

        Assert.Equal(
            "[builds/My App/release 1.0](https://artifacts.example.com/builds/My%20App/release%201.0)",
            Field(embed, "Target"));
    }

    [Fact]
    public void AppVeyor_github_provider_links_branch_and_full_commit()
    {
        WebhookRequest request = CreateAppVeyorRequest();
        request.EnvironmentVariables = new Dictionary<string, string>
        {
            ["appveyor_repo_provider"] = "gitHub"
        };
        JobProcessingResult result = new()
        {
            TargetSubDirectory = "builds/DsHidMini/master/1.2.3"
        };
        result.RecordArtifactSuccess();

        DiscordEmbed embed = Assert.Single(DiscordNotificationBuilder.Build(request, result).Embeds);

        Assert.Equal("[master](https://github.com/nefarius/DsHidMini/tree/master)", Field(embed, "Branch"));
        Assert.Equal("[abcdef0](https://github.com/nefarius/DsHidMini/commit/abcdef0123456789)",
            Field(embed, "Commit"));
        Assert.Contains("/commit/abcdef0123456789)", Field(embed, "Commit"));
    }

    [Fact]
    public void Unsupported_provider_keeps_branch_and_commit_as_text()
    {
        WebhookRequest request = CreateAppVeyorRequest();
        request.EnvironmentVariables = new Dictionary<string, string>
        {
            ["appveyor_repo_provider"] = "bitBucket"
        };
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();

        DiscordEmbed embed = Assert.Single(DiscordNotificationBuilder.Build(request, result).Embeds);

        Assert.Equal("master", Field(embed, "Branch"));
        Assert.Equal("abcdef0", Field(embed, "Commit"));
    }

    [Fact]
    public void Branch_link_encodes_ref_segments()
    {
        WebhookRequest request = new()
        {
            Artifacts = [],
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["github_repository"] = "nefarius/DsHidMini",
                ["github_ref_name"] = "release 1.0",
                ["github_sha"] = "abcdef0123456789"
            }
        };
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();

        DiscordEmbed embed = Assert.Single(DiscordNotificationBuilder.Build(request, result).Embeds);

        Assert.Equal("[release 1.0](https://github.com/nefarius/DsHidMini/tree/release%201.0)",
            Field(embed, "Branch"));
    }

    [Fact]
    public void ResolveBranch_prefers_github_head_ref_over_ref_name()
    {
        WebhookRequest request = new()
        {
            Artifacts = [],
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["github_repository"] = "nefarius/DsHidMini",
                ["github_head_ref"] = "feat/clickable-links",
                ["github_ref_name"] = "42/merge",
                ["github_sha"] = "abcdef0123456789"
            }
        };
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();

        DiscordEmbed embed = Assert.Single(DiscordNotificationBuilder.Build(request, result).Embeds);

        Assert.Equal(
            "[feat/clickable-links](https://github.com/nefarius/DsHidMini/tree/feat/clickable-links)",
            Field(embed, "Branch"));
    }

    [Fact]
    public void Oversized_target_markdown_link_falls_back_to_label()
    {
        string label = new('a', 496);
        string value = Field(EmbedWithTarget(label, "https://artifacts.example.com"), "Target");

        Assert.Equal(label, value);
        Assert.DoesNotContain("[", value, StringComparison.Ordinal);
        Assert.DoesNotContain("](", value, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_value_of_1024_characters_is_kept()
    {
        string raw = new('a', 1024);
        DiscordEmbed embed = EmbedWithTarget(raw);

        Assert.Equal(raw, Field(embed, "Target"));
        Assert.Equal(1024, Field(embed, "Target").Length);
    }

    [Fact]
    public void Field_value_of_1025_characters_is_truncated_to_1024()
    {
        string raw = new('a', 1025);
        string value = Field(EmbedWithTarget(raw), "Target");

        Assert.Equal(1024, value.Length);
        Assert.Equal(new string('a', 1023) + "…", value);
    }

    [Fact]
    public void Serialized_payload_disables_mentions()
    {
        WebhookRequest request = CreateAppVeyorRequest();
        JobProcessingResult result = new();
        result.RecordArtifactSuccess();

        string json = DiscordNotificationBuilder.Serialize(DiscordNotificationBuilder.Build(request, result));
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");
        Assert.Equal(JsonValueKind.Array, parse.ValueKind);
        Assert.Equal(0, parse.GetArrayLength());
        Assert.Equal(DiscordNotificationBuilder.SuccessColor,
            document.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32());
    }

    private static DiscordEmbed EmbedWithTarget(string target, string? publicArtifactsBaseUrl = null)
    {
        WebhookRequest request = CreateAppVeyorRequest();
        JobProcessingResult result = new()
        {
            TargetSubDirectory = target
        };
        result.RecordArtifactSuccess();
        return Assert.Single(DiscordNotificationBuilder.Build(request, result, publicArtifactsBaseUrl).Embeds);
    }

    private static string Field(DiscordEmbed embed, string name)
    {
        return embed.Fields.Single(field => field.Name == name).Value;
    }

    private static WebhookRequest CreateAppVeyorRequest()
    {
        return new WebhookRequest
        {
            ProjectName = "DsHidMini",
            RepositoryName = "nefarius/DsHidMini",
            BuildVersion = "1.2.3",
            BuildNumber = 12,
            Branch = "master",
            CommitId = "abcdef0123456789",
            CommitMessage = "Ship it",
            Artifacts = [],
            EnvironmentVariables = []
        };
    }
}
