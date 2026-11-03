using CloudShield.Application;
using CloudShield.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCloudShieldApplication();
builder.Services.AddCloudShieldInfrastructure();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShield.Api",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.Run();

public partial class Program;
