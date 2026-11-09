// =============================================================================
// EntitleX Educational Guide 07 — ASP.NET Core Architecture Guided Tour
// =============================================================================
//
// HOW TO USE THIS FILE
// --------------------
// Standalone teaching tour mirroring src/EntitleX.Api. Inline stubs replace
// real MediatR/EF/JWT packages so the mental model is clear in one file.
//
// REAL REPO ANCHORS
// -----------------
//   src/EntitleX.Api/Program.cs
//   src/EntitleX.Api/Middleware/CorrelationIdMiddleware.cs
//   src/EntitleX.Api/Infrastructure/ExceptionHandlingExtensions.cs
//   src/EntitleX.Api/Endpoints/LicenseExtensionEndpoints.cs
//   src/EntitleX.Api/Endpoints/ApprovalEndpoints.cs
//   src/EntitleX.Api/Endpoints/PendingApprovalEndpoints.cs
//   src/EntitleX.Api/Endpoints/LicenseExtensionRetryEndpoints.cs
//   src/EntitleX.Api/Endpoints/ReconciliationEndpoints.cs
//   src/EntitleX.Api/Endpoints/SagaEndpoints.cs
//   src/EntitleX.Api/Endpoints/OpsEndpoints.cs
//   src/EntitleX.Api/Endpoints/DevTokenEndpoints.cs
//   src/EntitleX.Infrastructure/Identity/AuthenticationServiceCollectionExtensions.cs
//   src/EntitleX.Application/Common/Behaviors/ValidationBehavior.cs
//   src/EntitleX.Application/DependencyInjection.cs
//   src/EntitleX.Messaging/Options/MessagingOptions.cs
//   src/EntitleX.Infrastructure/Providers/ProviderOptions.cs
//
// TABLE OF CONTENTS
// -----------------
//   PART 0  — Stubs + DemoHost
//   PART 1  — Host bootstrap (WebApplication / Serilog)
//   PART 2  — Middleware pipeline order
//   PART 3  — CorrelationIdMiddleware deep dive
//   PART 4  — Exception handler + ProblemDetails
//   PART 5  — CORS, AuthN (JWT Bearer), AuthZ policies
//   PART 6  — Minimal APIs MapGroup endpoint modules
//   PART 7  — MediatR CQRS + FluentValidation ValidationBehavior
//   PART 8  — Endpoint → sender.Send wiring samples
//   PART 9  — Health checks, metrics, OpenTelemetry
//   PART 10 — DI lifetimes (DbContext, workers, singletons)
//   PART 11 — Options pattern (MessagingOptions / ProviderOptions / JwtOptions)
//   PART 12 — RBAC roles walkthrough
//   PART 13 — Outbox + reconciliation gate from the API angle
//   PART 14 — Interview notes
//
// =============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace EntitleX.Guides.AspNetCoreArchitecture
{

#region PART 0 — Minimal stubs (stand-ins for ASP.NET / MediatR types)

namespace Domain
{
    public static class EntitleXRoles
    {
        public const string RenewalRepresentative = "RenewalRepresentative";
        public const string RenewalManager = "RenewalManager";
        public const string DealDeskManager = "DealDeskManager";
        public const string PlatformAdmin = "PlatformAdmin";
    }

    public static class AuthorizationPolicies
    {
        public const string CanCreateRequests = "CanCreateRequests";
        public const string CanDecideApprovals = "CanDecideApprovals";
        public const string CanReadApprovals = "CanReadApprovals";
        public const string CanManageReconciliation = "CanManageReconciliation";
    }

    public enum ApprovalBand { AutoApprove, RenewalManagerOnly, RenewalManagerThenDealDesk, Unsupported }

    public static class ApprovalRules
    {
        public static ApprovalBand Evaluate(int days) => days switch
        {
            < 1 => throw new ArgumentOutOfRangeException(nameof(days)),
            <= 30 => ApprovalBand.AutoApprove,
            <= 90 => ApprovalBand.RenewalManagerOnly,
            <= 365 => ApprovalBand.RenewalManagerThenDealDesk,
            _ => ApprovalBand.Unsupported
        };
    }
}

/// <summary>Toy DI container for teaching lifetimes.</summary>
public sealed class ToyServiceCollection
{
    private readonly List<(Type Service, Type Impl, string Lifetime)> _regs = new();
    public void AddSingleton<TService, TImpl>() where TImpl : TService =>
        _regs.Add((typeof(TService), typeof(TImpl), "Singleton"));
    public void AddScoped<TService, TImpl>() where TImpl : TService =>
        _regs.Add((typeof(TService), typeof(TImpl), "Scoped"));
    public void AddTransient<TService, TImpl>() where TImpl : TService =>
        _regs.Add((typeof(TService), typeof(TImpl), "Transient"));
    public void AddScoped<TService>(Func<TService> _factory) =>
        _regs.Add((typeof(TService), typeof(TService), "Scoped"));
    public IReadOnlyList<(Type Service, Type Impl, string Lifetime)> Registrations => _regs;
}

public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default);
}

public interface IRequest<TResponse> { }

public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken ct);
}

public interface IPipelineBehavior<TRequest, TResponse>
{
    Task<TResponse> Handle(TRequest request, Func<Task<TResponse>> next, CancellationToken ct);
}

public sealed class ToyMediator : ISender
{
    private readonly Dictionary<Type, object> _handlers = new();
    private readonly List<object> _behaviors = new();

    public void Register<TRequest, TResponse>(IRequestHandler<TRequest, TResponse> handler)
        where TRequest : IRequest<TResponse> =>
        _handlers[typeof(TRequest)] = handler;

    public void AddBehavior(object behavior) => _behaviors.Add(behavior);

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        var requestType = request.GetType();
        if (!_handlers.TryGetValue(requestType, out var handlerObj))
            throw new InvalidOperationException($"No handler for {requestType.Name}");

        Func<Task<TResponse>> next = () =>
        {
            dynamic h = handlerObj;
            return (Task<TResponse>)h.Handle((dynamic)request, ct);
        };

        foreach (var behavior in _behaviors.AsEnumerable().Reverse())
        {
            var b = behavior;
            var captured = next;
            next = () =>
            {
                dynamic dyn = b;
                return (Task<TResponse>)dyn.Handle((dynamic)request, captured, ct);
            };
        }

        return await next();
    }
}

#endregion

public static class DemoHost
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX ASP.NET Core Architecture Guide ===\n");
        Part1_HostBootstrap.Run();
        Part2_MiddlewarePipeline.Run();
        Part3_CorrelationId.Run();
        Part4_ExceptionHandler.Run();
        Part5_Auth.Run();
        Part6_MinimalApis.Run();
        await Part7_MediatR.RunAsync();
        await Part8_EndpointWiring.RunAsync();
        Part9_HealthMetrics.Run();
        Part10_DiLifetimes.Run();
        Part11_Options.Run();
        Part12_RbacWalkthrough.Run();
        Part13_OutboxAndReconciliation.Run();
        Part14_Interview.Print();
    }
}

// =============================================================================
// PART 1 — Host bootstrap
// =============================================================================
/// <summary>
/// Mirrors src/EntitleX.Api/Program.cs top-of-file Serilog bootstrap +
/// WebApplication.CreateBuilder → AddApplication → AddInfrastructure.
/// </summary>
public static class Part1_HostBootstrap
{
    public static void Run()
    {
        Console.WriteLine("-- PART 1: Host bootstrap --");
        Console.WriteLine("""
  // Simplified mental model of Program.cs:
  //
  // Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
  // var builder = WebApplication.CreateBuilder(args);
  // builder.Host.UseSerilog(... Enrich.WithProperty("Service", "EntitleX.Api"));
  // builder.Services.AddEntitleXOpenTelemetry(..., "EntitleX.Api", isAspNetCoreHost: true);
  // builder.Services.AddApplication();       // MediatR + ValidationBehavior
  // builder.Services.AddInfrastructure(cfg); // EF, JWT, providers, messaging
  // builder.Services.AddSwaggerGen(... Bearer security scheme ...);
  // builder.Services.AddProblemDetails();
  // builder.Services.AddCors("FrontendDev" → localhost:5173);
  // var app = builder.Build();
  // … middleware + Map*Endpoints …
  // if (Database:MigrateOnStartup) await db.Database.MigrateAsync();
  // await app.RunAsync();
""");
        Console.WriteLine("  Service name enricher: EntitleX.Api");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 2 — Middleware pipeline order
// =============================================================================
/// <summary>
/// CRITICAL: order matches Program.cs exactly.
/// CorrelationId → Serilog request logging → Exception handler → CORS → AuthN → AuthZ
/// </summary>
public static class Part2_MiddlewarePipeline
{
    public static void Run()
    {
        Console.WriteLine("-- PART 2: Middleware pipeline --");
        var pipeline = new[]
        {
            "1. CorrelationIdMiddleware          // X-Correlation-ID in/out + LogContext",
            "2. UseSerilogRequestLogging()       // structured HTTP request logs",
            "3. UseEntitleXExceptionHandler()    // ProblemDetails mapping",
            "4. UseCors(\"FrontendDev\")           // Vite dev origin",
            "5. UseAuthentication()              // JWT Bearer",
            "6. UseAuthorization()               // policies / roles",
            "7. MapSwagger (Development only)",
            "8. MapHealth + Map*Endpoints()",
        };
        foreach (var step in pipeline) Console.WriteLine("  " + step);

        Console.WriteLine("""
  INTERVIEW: Why CorrelationId BEFORE Serilog?
  So every request log line already carries CorrelationId from LogContext.
  Why exception handler BEFORE AuthZ?
  So auth failures and domain errors share ProblemDetails formatting —
  actually EntitleX registers the exception handler early so uncaught
  exceptions from later middleware/endpoints are caught consistently.
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 3 — CorrelationIdMiddleware
// =============================================================================
/// <summary>See src/EntitleX.Api/Middleware/CorrelationIdMiddleware.cs</summary>
public static class Part3_CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";

    public static void Run()
    {
        Console.WriteLine("-- PART 3: CorrelationIdMiddleware --");
        var incoming = (string?)null; // simulate missing header
        var correlationId = !string.IsNullOrWhiteSpace(incoming)
            ? incoming!.Trim()
            : Guid.NewGuid().ToString("N");
        Console.WriteLine($"  assigned CorrelationId={correlationId}");
        Console.WriteLine("  stored in: HttpContext.Items, Response header, CorrelationContext.Current, Serilog LogContext");
        Console.WriteLine("  Endpoints read it, e.g. ApprovalEndpoints / EntitlementAggregationEndpoints");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 4 — Exception handler
// =============================================================================
public static class Part4_ExceptionHandler
{
    public static void Run()
    {
        Console.WriteLine("-- PART 4: Exception handler + ProblemDetails --");
        Console.WriteLine("""
  UseEntitleXExceptionHandler maps:
    ValidationException          → 400 ValidationProblem
    DomainException              → 400/409 ProblemDetails (domain rule)
    UnauthorizedApproval…        → 403
    DbUpdateConcurrencyException → 409 (row_version conflict)
    NotFound                     → 404
    Unhandled                    → 500 (logged with CorrelationId)

  AddProblemDetails() enables RFC 7807 payloads for minimal APIs.
""");
        // Toy mapping
        foreach (var (ex, code) in new[]
                 {
                     ("ValidationException", 400),
                     ("DomainException: Unsupported band", 400),
                     ("DbUpdateConcurrencyException", 409),
                     ("KeyNotFoundException", 404),
                 })
            Console.WriteLine($"  {ex,-42} → HTTP {code}");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 5 — CORS + JWT + policies
// =============================================================================
/// <summary>
/// Real policy registration:
/// src/EntitleX.Infrastructure/Identity/AuthenticationServiceCollectionExtensions.cs
/// </summary>
public static class Part5_Auth
{
    public static void Run()
    {
        Console.WriteLine("-- PART 5: CORS / JWT / Authorization policies --");
        Console.WriteLine("  CORS FrontendDev origins: http://localhost:5173, http://127.0.0.1:5173");
        Console.WriteLine("  JWT: ValidateIssuer/Audience/Lifetime/SigningKey; RoleClaimType=ClaimTypes.Role");
        Console.WriteLine("  FallbackPolicy = RequireAuthenticatedUser (anonymous only on /health* + DevToken)");

        var policies = new Dictionary<string, string[]>
        {
            [Domain.AuthorizationPolicies.CanCreateRequests] = new[]
            {
                Domain.EntitleXRoles.RenewalRepresentative,
                Domain.EntitleXRoles.PlatformAdmin
            },
            [Domain.AuthorizationPolicies.CanDecideApprovals] = new[]
            {
                Domain.EntitleXRoles.RenewalManager,
                Domain.EntitleXRoles.DealDeskManager,
                Domain.EntitleXRoles.PlatformAdmin
            },
            [Domain.AuthorizationPolicies.CanReadApprovals] = new[] { "(any authenticated)" },
            [Domain.AuthorizationPolicies.CanManageReconciliation] = new[]
            {
                Domain.EntitleXRoles.PlatformAdmin
            },
        };

        foreach (var (policy, roles) in policies)
            Console.WriteLine($"  {policy}: {string.Join(", ", roles)}");

        // Simulate token role check
        var userRoles = new[] { Domain.EntitleXRoles.RenewalManager };
        Console.WriteLine($"  user roles=[{string.Join(",", userRoles)}]");
        Console.WriteLine($"  CanDecideApprovals? {userRoles.Any(r => policies[Domain.AuthorizationPolicies.CanDecideApprovals].Contains(r))}");
        Console.WriteLine($"  CanCreateRequests? {userRoles.Any(r => policies[Domain.AuthorizationPolicies.CanCreateRequests].Contains(r))}");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 6 — Minimal APIs MapGroup
// =============================================================================
public static class Part6_MinimalApis
{
    public static void Run()
    {
        Console.WriteLine("-- PART 6: Minimal APIs MapGroup --");
        var routes = new (string Group, string Method, string Path, string Policy, string File)[]
        {
            ("/api/v1/license-extension-requests", "POST", "/", "CanCreateRequests", "LicenseExtensionEndpoints.cs"),
            ("/api/v1/license-extension-requests", "GET", "/{id}", "(authenticated)", "LicenseExtensionEndpoints.cs"),
            ("/api/v1/license-extension-requests", "POST", "/{id}/retry", "CanCreateRequests", "LicenseExtensionRetryEndpoints.cs"),
            ("/api/v1/approvals", "POST", "/", "CanCreateRequests", "ApprovalEndpoints.cs"),
            ("/api/v1/approvals", "POST", "/{id}/decisions", "CanDecideApprovals", "ApprovalEndpoints.cs"),
            ("/api/v1/approvals", "GET", "/pending", "CanDecideApprovals", "PendingApprovalEndpoints.cs"),
            ("/api/v1/sagas", "GET", "/{requestId}", "CanReadApprovals", "SagaEndpoints.cs"),
            ("/api/v1/reconciliation", "GET/POST", "…", "CanManageReconciliation", "ReconciliationEndpoints.cs"),
            ("/api/v1/ops", "GET", "…", "CanManageReconciliation", "OpsEndpoints.cs"),
            ("/api/v1/dev/token", "POST", "/", "AllowAnonymous (Dev)", "DevTokenEndpoints.cs"),
        };

        foreach (var r in routes)
            Console.WriteLine($"  {r.Method,-4} {r.Group}{r.Path,-24} [{r.Policy}]  // {r.File}");

        Console.WriteLine("""
  Pattern:
    var group = app.MapGroup("/api/v1/...").WithTags("...");
    group.MapPost("/", Handler)
         .RequireAuthorization(AuthorizationPolicies.CanCreateRequests)
         .Produces(201).ProducesValidationProblem();
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 7 — MediatR + ValidationBehavior
// =============================================================================
public sealed record CreateLicenseExtensionRequestCommand(int MaxExtensionDays, string IdempotencyKey)
    : IRequest<CreateLicenseExtensionRequestResult>;

public sealed record CreateLicenseExtensionRequestResult(Guid Id, Domain.ApprovalBand Band);

public sealed class CreateLicenseExtensionRequestHandler
    : IRequestHandler<CreateLicenseExtensionRequestCommand, CreateLicenseExtensionRequestResult>
{
    public Task<CreateLicenseExtensionRequestResult> Handle(
        CreateLicenseExtensionRequestCommand request,
        CancellationToken ct)
    {
        var band = Domain.ApprovalRules.Evaluate(request.MaxExtensionDays);
        if (band == Domain.ApprovalBand.Unsupported)
            throw new InvalidOperationException(">365 days unsupported");
        return Task.FromResult(new CreateLicenseExtensionRequestResult(Guid.NewGuid(), band));
    }
}

/// <summary>
/// Mirrors src/EntitleX.Application/Common/Behaviors/ValidationBehavior.cs
/// Registered via cfg.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;)).
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly Func<TRequest, IEnumerable<string>> _validate;
    public ValidationBehavior(Func<TRequest, IEnumerable<string>> validate) => _validate = validate;

    public async Task<TResponse> Handle(
        TRequest request,
        Func<Task<TResponse>> next,
        CancellationToken ct)
    {
        var failures = _validate(request).ToList();
        if (failures.Count > 0)
            throw new InvalidOperationException("Validation: " + string.Join("; ", failures));
        return await next();
    }
}

public static class Part7_MediatR
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 7: MediatR CQRS + ValidationBehavior --");
        var mediator = new ToyMediator();
        mediator.Register(new CreateLicenseExtensionRequestHandler());
        mediator.AddBehavior(new ValidationBehavior<CreateLicenseExtensionRequestCommand, CreateLicenseExtensionRequestResult>(
            cmd =>
            {
                var errors = new List<string>();
                if (cmd.MaxExtensionDays < 1) errors.Add("MaxExtensionDays ≥ 1");
                if (string.IsNullOrWhiteSpace(cmd.IdempotencyKey)) errors.Add("IdempotencyKey required");
                return errors;
            }));

        var ok = await mediator.Send(new CreateLicenseExtensionRequestCommand(45, "idem-1"));
        Console.WriteLine($"  created Id={ok.Id} Band={ok.Band}");

        try
        {
            await mediator.Send(new CreateLicenseExtensionRequestCommand(0, ""));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  validation blocked: {ex.Message}");
        }

        Console.WriteLine("  CQRS split: Commands mutate (create/decide/retry); Queries read (get/pending/saga).");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 8 — Endpoint → sender.Send
// =============================================================================
public static class Part8_EndpointWiring
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- PART 8: Endpoint → ISender.Send --");

        // Mirror LicenseExtensionEndpoints.CreateAsync
        var mediator = new ToyMediator();
        mediator.Register(new CreateLicenseExtensionRequestHandler());
        mediator.AddBehavior(new ValidationBehavior<CreateLicenseExtensionRequestCommand, CreateLicenseExtensionRequestResult>(
            cmd => cmd.MaxExtensionDays < 1 ? new[] { "bad days" } : Array.Empty<string>()));

        async Task<object> CreateAsync(CreateLicenseExtensionRequestCommand command, ISender sender, CancellationToken ct)
        {
            var result = await sender.Send(command, ct);
            // Results.Created($"/api/v1/license-extension-requests/{result.Id}", result);
            return new { status = 201, location = $"/api/v1/license-extension-requests/{result.Id}", result.Band };
        }

        var response = await CreateAsync(
            new CreateLicenseExtensionRequestCommand(120, "idem-dd"),
            mediator,
            CancellationToken.None);
        Console.WriteLine($"  HTTP response: {response}");

        // Pending approvals keyset query params (PendingApprovalEndpoints)
        Console.WriteLine("  GET /api/v1/approvals/pending?role=RenewalManager&take=50&afterCreatedAtUtc=…&afterApprovalCaseId=…");
        Console.WriteLine("  Retry body may include LogicalOperationIds filter (selective retry).");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 9 — Health + metrics
// =============================================================================
public static class Part9_HealthMetrics
{
    public static void Run()
    {
        Console.WriteLine("-- PART 9: Health / metrics / OTel --");
        Console.WriteLine("""
  MapGet("/health")               → liveness JSON { status, service=EntitleX.Api }
  MapHealthChecks("/health/ready")→ readiness (DB etc.)
  MapHealthChecks("/health/providers", Predicate tag=="provider")
      → cloudshield + networkguard registrations from AddInfrastructure
  MapEntitleXPrometheusScrapingEndpoint()
  AddEntitleXOpenTelemetry(..., isAspNetCoreHost: true)

  Workers also expose backlog probes:
    BacklogMetricsProbe (outbox depth)
    SqsDepthProbeHostedService
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 10 — DI lifetimes
// =============================================================================
public static class Part10_DiLifetimes
{
    public static void Run()
    {
        Console.WriteLine("-- PART 10: DI lifetimes --");
        var services = new ToyServiceCollection();

        // From Infrastructure DependencyInjection (conceptual registration list)
        services.AddScoped<IEntitleXDbContext, EntitleXDbContextStub>();
        services.AddScoped<IUnitOfWork, EntitleXDbContextStub>();
        services.AddScoped<IOutboxWriter, OutboxWriterStub>();
        services.AddTransient<IProviderEntitlementClient, CloudShieldClientStub>();
        services.AddTransient<IProviderEntitlementClient, NetworkGuardClientStub>();
        services.AddSingleton<IJwtTokenService, JwtTokenServiceStub>();

        Console.WriteLine("  Rule of thumb:");
        Console.WriteLine("    Scoped   → DbContext, repositories, OutboxClaimer, request services");
        Console.WriteLine("    Transient→ provider HTTP clients (or typed HttpClient)");
        Console.WriteLine("    Singleton→ JwtTokenService, meters, options monitors");
        Console.WriteLine("  NEVER: Singleton holding Scoped DbContext (captive dependency).");
        Console.WriteLine("  Workers use AddInfrastructureForWorkers (no JWT/email).");
        Console.WriteLine("  Lambdas use AddInfrastructureForLambdas (DB + email + providers, no JWT).");

        foreach (var r in services.Registrations)
            Console.WriteLine($"  {r.Lifetime,-10} {r.Service.Name} → {r.Impl.Name}");
        Console.WriteLine();
    }

    public interface IEntitleXDbContext { }
    public interface IUnitOfWork { }
    public interface IOutboxWriter { }
    public interface IProviderEntitlementClient { }
    public interface IJwtTokenService { }
    public sealed class EntitleXDbContextStub : IEntitleXDbContext, IUnitOfWork { }
    public sealed class OutboxWriterStub : IOutboxWriter { }
    public sealed class CloudShieldClientStub : IProviderEntitlementClient { }
    public sealed class NetworkGuardClientStub : IProviderEntitlementClient { }
    public sealed class JwtTokenServiceStub : IJwtTokenService { }
}

// =============================================================================
// PART 11 — Options pattern
// =============================================================================
public static class Part11_Options
{
    public static void Run()
    {
        Console.WriteLine("-- PART 11: Options pattern --");

        var messaging = new MessagingOptions
        {
            Sqs = new SqsOptions { FulfillmentQueueName = "entitlex-fulfillment", VisibilityTimeoutSeconds = 30 },
            Kafka = new KafkaOptions
            {
                CloudShieldCommandsTopic = "cloudshield.license-extension.commands",
                NetworkGuardCommandsTopic = "networkguard.license-extension.commands",
                ProviderResultsTopic = "entitlex.provider.results",
            },
            Outbox = new OutboxOptions { BatchSize = 20, LockDurationSeconds = 30, MaxAttempts = 8 },
        };
        var providers = new ProviderOptions
        {
            CloudShield = new ProviderEndpointOptions { Timeout = TimeSpan.FromSeconds(5), BulkheadMaxParallelization = 32 },
            NetworkGuard = new ProviderEndpointOptions { Timeout = TimeSpan.FromSeconds(5), BulkheadMaxParallelization = 32 },
        };
        var jwt = new JwtOptions { Issuer = "entitlex", Audience = "entitlex-api", EnableDevTokenEndpoint = true };

        Console.WriteLine($"  Messaging:Kafka:CloudShieldCommandsTopic = {messaging.Kafka.CloudShieldCommandsTopic}");
        Console.WriteLine($"  Messaging:Outbox:BatchSize = {messaging.Outbox.BatchSize}");
        Console.WriteLine($"  Providers:CloudShield:Bulkhead = {providers.CloudShield.BulkheadMaxParallelization}");
        Console.WriteLine($"  Jwt:EnableDevTokenEndpoint = {jwt.EnableDevTokenEndpoint}");
        Console.WriteLine("  Bind via services.Configure<T>(configuration.GetSection(T.SectionName));");
        Console.WriteLine();
    }

    // Mirrors src/EntitleX.Messaging/Options/MessagingOptions.cs (trimmed)
    public sealed class MessagingOptions
    {
        public const string SectionName = "Messaging";
        public SqsOptions Sqs { get; set; } = new();
        public KafkaOptions Kafka { get; set; } = new();
        public OutboxOptions Outbox { get; set; } = new();
    }
    public sealed class SqsOptions
    {
        public string FulfillmentQueueName { get; set; } = "entitlex-fulfillment";
        public int VisibilityTimeoutSeconds { get; set; } = 30;
    }
    public sealed class KafkaOptions
    {
        public string CloudShieldCommandsTopic { get; set; } = "cloudshield.license-extension.commands";
        public string NetworkGuardCommandsTopic { get; set; } = "networkguard.license-extension.commands";
        public string ProviderResultsTopic { get; set; } = "entitlex.provider.results";
    }
    public sealed class OutboxOptions
    {
        public int BatchSize { get; set; } = 20;
        public int LockDurationSeconds { get; set; } = 30;
        public int MaxAttempts { get; set; } = 8;
    }
    public sealed class ProviderOptions
    {
        public const string SectionName = "Providers";
        public ProviderEndpointOptions CloudShield { get; set; } = new();
        public ProviderEndpointOptions NetworkGuard { get; set; } = new();
    }
    public sealed class ProviderEndpointOptions
    {
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
        public int BulkheadMaxParallelization { get; set; } = 32;
        public int BulkheadQueueLimit { get; set; } = 64;
    }
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";
        public string Issuer { get; set; } = "entitlex";
        public string Audience { get; set; } = "entitlex-api";
        public bool EnableDevTokenEndpoint { get; set; }
    }
}

// =============================================================================
// PART 12 — RBAC walkthrough
// =============================================================================
public static class Part12_RbacWalkthrough
{
    public static void Run()
    {
        Console.WriteLine("-- PART 12: RBAC walkthrough --");
        var personas = new (string Name, string Role, string Action, bool Allowed)[]
        {
            ("Rina Rep", Domain.EntitleXRoles.RenewalRepresentative, "POST license-extension-requests", true),
            ("Rina Rep", Domain.EntitleXRoles.RenewalRepresentative, "POST approvals/{id}/decisions", false),
            ("Raj RM", Domain.EntitleXRoles.RenewalManager, "GET approvals/pending", true),
            ("Raj RM", Domain.EntitleXRoles.RenewalManager, "Decide RM stage", true),
            ("Dana DD", Domain.EntitleXRoles.DealDeskManager, "Decide DealDesk stage", true),
            ("Dana DD", Domain.EntitleXRoles.DealDeskManager, "Manage reconciliation", false),
            ("Pat Admin", Domain.EntitleXRoles.PlatformAdmin, "Everything above + ops/recon", true),
        };
        foreach (var p in personas)
            Console.WriteLine($"  {p.Name,-10} {p.Role,-22} {p.Action,-40} {(p.Allowed ? "ALLOW" : "DENY")}");

        Console.WriteLine("  Dev tokens: DevTokenEndpoints maps role → sample user ids (user-dd-001, …).");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 13 — Outbox + reconciliation from API angle
// =============================================================================
public static class Part13_OutboxAndReconciliation
{
    public static void Run()
    {
        Console.WriteLine("-- PART 13: Outbox + reconciliation gate (API view) --");
        Console.WriteLine("""
  Create/Decide handlers write domain changes + OutboxMessages in ONE DbContext
  transaction (IUnitOfWork.SaveChangesAsync). EntitleX.OutboxPublisher worker
  claims rows (FOR UPDATE SKIP LOCKED) and publishes to SQS/Kafka.

  When saga enters ReconciliationRequired:
    - API ReconciliationEndpoints (CanManageReconciliation / PlatformAdmin)
    - OpsEndpoints for operator tooling
    - Do NOT silently re-queue fulfillment until gate cleared

  Selective retry:
    LicenseExtensionRetryEndpoints → command with optional LogicalOperationIds
    → FailedItemSelector → new AttemptId + ParentId lineage → new saga generation
""");
        Console.WriteLine();
    }
}

// =============================================================================
// PART 14 — Interview notes
// =============================================================================
public static class Part14_Interview
{
    public static void Print()
    {
        Console.WriteLine("-- PART 14: Interview notes --");
        Console.WriteLine("""
  1. Recite middleware order from Program.cs without looking it up.
  2. Explain FallbackPolicy vs per-endpoint RequireAuthorization(policy).
  3. Why Minimal APIs MapGroup modules instead of Controllers here?
     → thin endpoints, vertical slices via MediatR, less ceremony.
  4. ValidationBehavior is cross-cutting — handlers assume valid input.
  5. DbContext scoped per request; workers create scopes per poll iteration.
  6. Options: MessagingOptions vs ProviderOptions vs JwtOptions sections.
  7. Health: /health vs /health/ready vs /health/providers.
  8. CorrelationId must flow into outbox envelopes / Kafka headers.
  9. Know which roles bind to CanCreateRequests / CanDecideApprovals /
     CanManageReconciliation.
  10. MigrateOnStartup is opt-in config — not default in production blindly.
""");
    }
}
}
