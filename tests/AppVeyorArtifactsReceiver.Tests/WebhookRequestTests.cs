using AppVeyorArtifactsReceiver.Models;

namespace AppVeyorArtifactsReceiver.Tests;

public sealed class WebhookRequestTests
{
    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData(" true ")]
    public void ShouldSkipLatestSymlink_is_true_for_boolean_true_values(string value)
    {
        WebhookRequest request = RequestWithSkipValue(value);

        Assert.True(request.ShouldSkipLatestSymlink());
    }

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData("FALSE")]
    [InlineData(" false ")]
    public void ShouldSkipLatestSymlink_is_false_for_boolean_false_values(string value)
    {
        WebhookRequest request = RequestWithSkipValue(value);

        Assert.False(request.ShouldSkipLatestSymlink());
    }

    [Fact]
    public void ShouldSkipLatestSymlink_is_false_when_the_key_is_missing()
    {
        WebhookRequest request = new()
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                ["appveyor_project_name"] = "DsHidMini"
            }
        };

        Assert.False(request.ShouldSkipLatestSymlink());
    }

    [Fact]
    public void ShouldSkipLatestSymlink_is_false_when_environment_variables_are_null()
    {
        WebhookRequest request = new();

        Assert.False(request.ShouldSkipLatestSymlink());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("skip")]
    public void ShouldSkipLatestSymlink_is_false_for_malformed_values(string value)
    {
        WebhookRequest request = RequestWithSkipValue(value);

        Assert.False(request.ShouldSkipLatestSymlink());
    }

    private static WebhookRequest RequestWithSkipValue(string value)
    {
        return new WebhookRequest
        {
            EnvironmentVariables = new Dictionary<string, string>
            {
                [WebhookRequest.SkipLatestSymlinkEnvironmentVariable] = value
            }
        };
    }
}
