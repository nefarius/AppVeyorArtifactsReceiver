﻿using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace AppVeyorArtifactsReceiver.Models;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public sealed class WebhookRequest
{
    [RouteParam]
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("accountName")]
    public string AccountName { get; set; }

    [JsonPropertyName("projectId")]
    public int ProjectId { get; set; }

    [JsonPropertyName("projectName")]
    public string ProjectName { get; set; }

    [JsonPropertyName("projectSlug")]
    public string ProjectSlug { get; set; }

    [JsonPropertyName("buildId")]
    public int BuildId { get; set; }

    [JsonPropertyName("buildNumber")]
    public int BuildNumber { get; set; }

    [JsonPropertyName("buildVersion")]
    public string BuildVersion { get; set; }

    [JsonPropertyName("buildJobId")]
    public string BuildJobId { get; set; }

    [JsonPropertyName("jobId")]
    public string JobId { get; set; }

    [JsonPropertyName("repositoryName")]
    public string RepositoryName { get; set; }

    [JsonPropertyName("branch")]
    public string Branch { get; set; }

    [JsonPropertyName("commitId")]
    public string CommitId { get; set; }

    [JsonPropertyName("commitAuthor")]
    public string CommitAuthor { get; set; }

    [JsonPropertyName("commitAuthorEmail")]
    public string CommitAuthorEmail { get; set; }

    [JsonPropertyName("commitDate")]
    public string CommitDate { get; set; }

    [JsonPropertyName("commitMessage")]
    public string CommitMessage { get; set; }

    [JsonPropertyName("commitMessageExtended")]
    public string CommitMessageExtended { get; set; }

    [JsonPropertyName("artifacts")]
    public List<Artifact> Artifacts { get; set; }

    [JsonPropertyName("environmentVariables")]
    public Dictionary<string, string> EnvironmentVariables { get; set; }
}