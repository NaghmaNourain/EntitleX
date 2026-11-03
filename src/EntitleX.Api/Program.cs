using EntitleX.Application;
using EntitleX.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "EntitleX.Api",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.Run();

public partial class Program;
