using EntitleX.Infrastructure;
using EntitleX.NotificationService;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddEntitleXEmail(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
