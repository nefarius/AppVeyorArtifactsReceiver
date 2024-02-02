global using FastEndpoints;

using AppVeyorArtifactsReceiver;
using AppVeyorArtifactsReceiver.Configuration;

using Nefarius.Utilities.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder().Setup();

builder.Services.AddAuthorization();
builder.Services.AddFastEndpoints();
builder.Services.AddHttpClient();

builder.Services.Configure<ServiceConfig>(builder.Configuration.GetSection(nameof(ServiceConfig)));


WebApplication app = builder.Build().Setup();

app.UseAuthorization();
app.UseFastEndpoints();

app.Run();
