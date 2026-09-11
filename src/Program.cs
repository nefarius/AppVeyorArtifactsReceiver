global using FastEndpoints;

using System.Net.Http.Headers;
using System.Reflection;

using AppVeyorArtifactsReceiver.Configuration;
using AppVeyorArtifactsReceiver.Models;
using AppVeyorArtifactsReceiver.Notifications;

using Nefarius.Utilities.AspNetCore;

using Polly;
using Polly.Contrib.WaitAndRetry;

using Serilog.Enrichers.Sensitive;

WebApplicationBuilder builder = WebApplication.CreateBuilder().Setup(options =>
{
    options.Serilog.Configuration.Enrich.WithSensitiveDataMasking(enricherOptions =>
    {
        enricherOptions.MaskProperties.Add(new MaskProperty
        {
            Name = nameof(WebhookRequest.GitHubToken), Options = MaskOptions.Default
        });
        enricherOptions.MaskProperties.Add(new MaskProperty
        {
            Name = nameof(TargetSettings.DiscordWebhookUrls), Options = MaskOptions.Default
        });
    });
});

builder.Services.AddAuthorization();
builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient();

builder.Services.Configure<ServiceConfig>(builder.Configuration.GetSection(nameof(ServiceConfig)));

builder.Services.AddHttpClient("AppVeyor",
        client =>
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                Assembly.GetEntryAssembly()?.GetName().Name!,
                Assembly.GetEntryAssembly()?.GetName().Version!.ToString()));
        })
    .AddTransientHttpErrorPolicy(pb =>
        pb.WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(3), 10)));

builder.Services.AddSingleton<DiscordWebhookNotifier>();
builder.Services.AddHttpClient(DiscordWebhookNotifier.HttpClientName,
        client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(
                Assembly.GetEntryAssembly()?.GetName().Name!,
                Assembly.GetEntryAssembly()?.GetName().Version!.ToString()));
        })
    .AddTransientHttpErrorPolicy(pb =>
        pb.WaitAndRetryAsync(Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), 2)));

WebApplication app = builder.Build().Setup();

app.UseAuthorization();
app.UseFastEndpoints();

app.Run();