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
        Assert.Equal("feat/discord", Field(embed, "Branch"));
        Assert.Equal("0123456", Field(embed, "Commit"));
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

    private static DiscordEmbed EmbedWithTarget(string target)
    {
        WebhookRequest request = CreateAppVeyorRequest();
        JobProcessingResult result = new()
        {
            TargetSubDirectory = target
        };
        result.RecordArtifactSuccess();
        return Assert.Single(DiscordNotificationBuilder.Build(request, result).Embeds);
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
