global using FastEndpoints;

using AppVeyorArtifactsReceiver;

using Serilog;
using Serilog.Core;

WebApplicationBuilder builder = WebApplication.CreateBuilder();
builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient();


builder.Services.Configure<ServiceConfig>(builder.Configuration.GetSection(nameof(ServiceConfig)));

#region Logging

Logger logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

// logger instance used by non-DI-code
Log.Logger = logger;

builder.Host.UseSerilog(logger);

builder.Services.AddLogging(b =>
{
    b.SetMinimumLevel(LogLevel.Information);
    b.AddSerilog(logger, true);
});

builder.Services.AddSingleton(new LoggerFactory().AddSerilog(logger));

#endregion

WebApplication app = builder.Build();

app.UseAuthorization();
app.UseFastEndpoints();

app.Run();