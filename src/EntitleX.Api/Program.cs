using EntitleX.Api.Endpoints;
using EntitleX.Api.Infrastructure;
using EntitleX.Application;
using EntitleX.Infrastructure;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "EntitleX API",
        Version = "v1",
        Description = "Enterprise license-extension control plane (Phase 2)."
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseEntitleXExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "Healthy",
    service = "EntitleX.Api",
    timestampUtc = DateTimeOffset.UtcNow
}));

app.MapLicenseExtensionEndpoints();

if (ShouldMigrateOnStartup(app.Configuration))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<EntitleXDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

static bool ShouldMigrateOnStartup(IConfiguration configuration) =>
    configuration.GetValue("Database:MigrateOnStartup", defaultValue: false);

public partial class Program;
