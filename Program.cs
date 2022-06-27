global using FastEndpoints;
using AppVeyorArtifactsReceiver;
using Serilog;

var builder = WebApplication.CreateBuilder();
builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient();

var config = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json").Build();

builder.Services.Configure<ServiceConfig>(config.GetSection(nameof(ServiceConfig)));

#region Logging

var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(config)
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

var app = builder.Build();
app.UseAuthorization();
app.UseFastEndpoints();
app.Run();