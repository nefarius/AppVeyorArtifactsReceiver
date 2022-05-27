global using FastEndpoints;
using AppVeyorArtifactsReceiver;

var builder = WebApplication.CreateBuilder();
builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient();

var config = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json").Build();

builder.Services.Configure<ServiceConfig>(config.GetSection(nameof(ServiceConfig)));

var app = builder.Build();
app.UseAuthorization();
app.UseFastEndpoints();
app.Run();
