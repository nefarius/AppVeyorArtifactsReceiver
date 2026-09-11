#nullable enable
using System.Text.Json.Serialization;

namespace AppVeyorArtifactsReceiver.Notifications;

internal sealed class DiscordWebhookPayload
{
    [JsonPropertyName("allowed_mentions")]
    public DiscordAllowedMentions AllowedMentions { get; init; } = new();

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed> Embeds { get; init; } = [];
}

internal sealed class DiscordAllowedMentions
{
    [JsonPropertyName("parse")]
    public string[] Parse { get; init; } = [];
}

internal sealed class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("color")]
    public int Color { get; init; }

    [JsonPropertyName("fields")]
    public List<DiscordEmbedField> Fields { get; init; } = [];
}

internal sealed class DiscordEmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("inline")]
    public bool Inline { get; init; }
}
