using NetworkGuard.Application;
using NetworkGuard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNetworkGuardApplication();
builder.Services.AddNetworkGuardInfrastructure();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "NetworkGuard.Api",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.Run();

public partial class Program;
