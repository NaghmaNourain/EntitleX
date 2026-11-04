using EntitleX.Infrastructure;
using EntitleX.ReconciliationWorker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddInfrastructureForWorkers(builder.Configuration);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
