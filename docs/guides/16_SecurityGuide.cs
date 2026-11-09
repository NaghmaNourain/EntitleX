// =====================================================================================
//  16_SecurityGuide.cs
//  EntitleX security guide — JWT Bearer HS256, RBAC, defense-in-depth approval checks,
//  idempotency, secrets hygiene, unsafe POST no-retry, HTTPS/CORS, correlation logging,
//  admin reconciliation authz, simulator trust boundaries, OWASP-ish checklist.
//
//  HOW TO RUN (needs .NET 8 SDK, NO NuGet packages):
//      dotnet new console -n SecurityGuide && cd SecurityGuide
//      replace Program.cs with this file, then:  dotnet run
//
//  HOW TO READ
//    CONCEPT / WHY / TRADE-OFF / DEMO. Simulations only use BCL crypto.
//    Real paths: src/EntitleX.Infrastructure/Identity/, src/EntitleX.Domain/Identity/,
//                src/EntitleX.Domain/Approval/, ProviderHttpClientRegistration.cs
//
//  TABLE OF CONTENTS
//   PART A  Authentication — JWT Bearer HS256
//     A1  SigningKey ≥ 32, issuer/audience/lifetime
//     A2  Token minting claims (roles) + validation checklist
//     A3  Dev token endpoint must never ship enabled
//   PART B  Authorization — RBAC + domain enforcement
//     B1  Policies (CanCreateRequests, CanDecideApprovals, CanManageReconciliation)
//     B2  Defense-in-depth: ApprovalCase.RecordDecision role check
//     B3  UnauthorizedApprovalTransitionException → HTTP 403
//     B4  Admin reconciliation endpoints
//   PART C  Request safety — idempotency & correlation
//     C1  Idempotency-Key on selective retry
//     C2  Correlation ids without leaking PII
//   PART D  Secrets, transport, CORS
//     D1  Secrets never in terraform state / git
//     D2  HTTPS metadata + CORS notes
//   PART E  Provider & simulator trust boundaries
//     E1  Provider POST no-retry (unsafe)
//     E2  Simulator trust boundaries
//   PART F  OWASP-ish checklist mapped to EntitleX
//   PART G  Interview Q&A
// =====================================================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EntitleX.Guides.Security;

public static class Program
{
    public static void Main()
    {
        PartA_Jwt();
        PartB_Rbac();
        PartC_IdempotencyAndCorrelation();
        PartD_SecretsHttpsCors();
        PartE_ProviderAndSimulators();
        PartF_OwaspChecklist();
        PartG_InterviewQa();

        Console.WriteLine();
        Console.WriteLine("All EntitleX security demos finished.");
    }

    // =================================================================================
    // PART A  JWT BEARER HS256
    // =================================================================================
    //
    // REAL
    //   JwtOptions / JwtTokenService / AuthenticationServiceCollectionExtensions
    //   under src/EntitleX.Infrastructure/Identity/
    //   Roles: EntitleXRoles (RenewalRepresentative, RenewalManager, DealDeskManager, PlatformAdmin)
    //
    // CONCEPT
    //   Symmetric HS256 JWT for the portfolio control plane. SigningKey must be ≥ 32 chars
    //   (256-bit material when UTF-8 encoded length is respected as a floor).
    // =================================================================================
    private static void PartA_Jwt()
    {
        Header("PART A  AUTHENTICATION — JWT BEARER HS256");
        A1_SigningKeyFloor();
        A2_MintAndValidate();
        A3_DevTokenEndpoint();
    }

    private static void A1_SigningKeyFloor()
    {
        Section("A1  SigningKey ≥ 32 characters (fail closed at startup)");

        foreach (var key in new[] { "short", "12345678901234567890123456789012", "" })
        {
            var ok = !string.IsNullOrWhiteSpace(key) && key.Length >= 32;
            Console.WriteLine($"  keyLen={key.Length,2} → {(ok ? "ACCEPT" : "REJECT")}  (real: InvalidOperationException at DI)");
        }

        Console.WriteLine("  Cite: AuthenticationServiceCollectionExtensions + JwtTokenService guards");
        Console.WriteLine("  Production: Jwt__SigningKey from Secrets Manager (modules/secrets).");
    }

    private static void A2_MintAndValidate()
    {
        Section("A2  Mint claims + validation checklist (simulation)");

        var signingKey = "entitlex-dev-signing-key-32chars!!"; // length ≥ 32
        var token = ToyJwt.Create(
            signingKey,
            userId: "user-rm-001",
            email: "renewal.manager@entitlex.local",
            roles: new[] { ToyRoles.RenewalManager },
            issuer: "EntitleX",
            audience: "EntitleX",
            lifetimeMinutes: 480);

        Console.WriteLine($"  token parts={token.Split('.').Length} (header.payload.sig)");

        // Happy path
        var principal = ToyJwt.Validate(token, signingKey, "EntitleX", "EntitleX");
        Console.WriteLine($"  valid sub={principal.UserId} roles=[{string.Join(",", principal.Roles)}]");

        // Classic attacks
        Reject("alg none", () =>
        {
            var forged = ToyJwt.ForgeAlgNone(principal.UserId, new[] { ToyRoles.PlatformAdmin });
            ToyJwt.Validate(forged, signingKey, "EntitleX", "EntitleX");
        });

        Reject("wrong key", () =>
            ToyJwt.Validate(token, "different-key-32-characters-long!", "EntitleX", "EntitleX"));

        Reject("wrong audience", () =>
            ToyJwt.Validate(token, signingKey, "EntitleX", "SomeoneElse"));

        Reject("expired", () =>
        {
            var expired = ToyJwt.Create(signingKey, "u", "u@x", new[] { ToyRoles.RenewalRepresentative },
                "EntitleX", "EntitleX", lifetimeMinutes: -5);
            ToyJwt.Validate(expired, signingKey, "EntitleX", "EntitleX");
        });

        Console.WriteLine("  REAL TokenValidationParameters: ValidateIssuer/Audience/Lifetime/SigningKey,");
        Console.WriteLine("  ClockSkew=1m, RoleClaimType=ClaimTypes.Role, RequireHttpsMetadata=true");
    }

    private static void A3_DevTokenEndpoint()
    {
        Section("A3  Dev token endpoint — Development only");

        Console.WriteLine("  Jwt:EnableDevTokenEndpoint=true exposes POST /api/v1/dev/tokens");
        Console.WriteLine("  Seed roles → user ids (README):");
        Console.WriteLine("    RenewalRepresentative → user-rep-001");
        Console.WriteLine("    RenewalManager        → user-rm-001");
        Console.WriteLine("    DealDeskManager       → user-dd-001");
        Console.WriteLine("    PlatformAdmin         → user-admin-001");
        Console.WriteLine("  TRADE-OFF: great for demos; MUST be false in any shared/AWS environment.");
        Console.WriteLine("  Unknown roles rejected by JwtTokenService (EntitleXRoles.All).");
    }

    // =================================================================================
    // PART B  AUTHORIZATION — RBAC + DOMAIN
    // =================================================================================
    //
    // Policies (AuthorizationPolicies):
    //   CanCreateRequests      → RenewalRepresentative, PlatformAdmin
    //   CanDecideApprovals     → RenewalManager, DealDeskManager, PlatformAdmin
    //   CanReadApprovals       → authenticated
    //   CanManageReconciliation→ PlatformAdmin
    // FallbackPolicy = RequireAuthenticatedUser
    //
    // Domain: ApprovalCase.RecordDecision checks stage RequiredRole OR PlatformAdmin,
    // throws UnauthorizedApprovalTransitionException (mapped to 403).
    // =================================================================================
    private static void PartB_Rbac()
    {
        Header("PART B  AUTHORIZATION — RBAC + DOMAIN ENFORCEMENT");
        B1_Policies();
        B2_DefenseInDepthApproval();
        B3_UnauthorizedException();
        B4_AdminReconciliation();
    }

    private static void B1_Policies()
    {
        Section("B1  ASP.NET policy matrix");

        var matrix = new (string Policy, string[] Roles)[]
        {
            ("CanCreateRequests", new[] { ToyRoles.RenewalRepresentative, ToyRoles.PlatformAdmin }),
            ("CanDecideApprovals", new[] { ToyRoles.RenewalManager, ToyRoles.DealDeskManager, ToyRoles.PlatformAdmin }),
            ("CanReadApprovals", new[] { "(any authenticated)" }),
            ("CanManageReconciliation", new[] { ToyRoles.PlatformAdmin }),
        };

        foreach (var (policy, roles) in matrix)
            Console.WriteLine($"  {policy,-28} → {string.Join(", ", roles)}");

        Console.WriteLine("  Cite: AuthenticationServiceCollectionExtensions.AddEntitleXAuthentication");
        Console.WriteLine("  UI hiding buttons is NOT authorization — policies + handlers + domain.");
    }

    private static void B2_DefenseInDepthApproval()
    {
        Section("B2  Defense-in-depth — handler/domain re-check roles");

        var approval = ToyApprovalCase.CreatePending(requiredRole: ToyRoles.RenewalManager);

        TryDecide(approval, actor: "user-rep-001", roles: new[] { ToyRoles.RenewalRepresentative }, expectOk: false);
        TryDecide(approval, actor: "user-rm-001", roles: new[] { ToyRoles.RenewalManager }, expectOk: true);

        var dealDesk = ToyApprovalCase.CreatePending(requiredRole: ToyRoles.DealDeskManager);
        TryDecide(dealDesk, actor: "user-rm-001", roles: new[] { ToyRoles.RenewalManager }, expectOk: false);
        TryDecide(dealDesk, actor: "user-admin-001", roles: new[] { ToyRoles.PlatformAdmin }, expectOk: true);

        Console.WriteLine("  WHY both policy and domain?");
        Console.WriteLine("    Policy gates HTTP entry; domain protects invariants if called from");
        Console.WriteLine("    another host/handler later. Fail closed.");
        Console.WriteLine("  Cite: ApprovalCase.RecordDecision, ApprovalRules.RoleName");
    }

    private static void B3_UnauthorizedException()
    {
        Section("B3  UnauthorizedApprovalTransitionException → 403");

        Console.WriteLine("  Distinct from DomainException so API maps:");
        Console.WriteLine("    UnauthorizedApprovalTransitionException → 403");
        Console.WriteLine("    other domain failures                   → 400/409/etc.");
        Console.WriteLine("  Cite: UnauthorizedApprovalTransitionException.cs");
        Console.WriteLine("        ExceptionHandlingExtensions.cs");
    }

    private static void B4_AdminReconciliation()
    {
        Section("B4  Admin reconciliation / ops endpoints");

        var endpoints = new[]
        {
            "ReconciliationEndpoints → RequireAuthorization(CanManageReconciliation)",
            "OpsEndpoints            → RequireAuthorization(CanManageReconciliation)",
        };
        foreach (var e in endpoints) Console.WriteLine($"  {e}");

        var gate = new PolicyGate();
        Console.WriteLine($"  Rep may reconcile? {gate.Allows(ToyRoles.RenewalRepresentative, "CanManageReconciliation")}");
        Console.WriteLine($"  Admin may reconcile? {gate.Allows(ToyRoles.PlatformAdmin, "CanManageReconciliation")}");
        Console.WriteLine("  Manual reconcile still respects ReconciliationGate (no blind POST).");
    }

    private static void TryDecide(ToyApprovalCase approval, string actor, string[] roles, bool expectOk)
    {
        try
        {
            approval.RecordDecision(actor, roles);
            Console.WriteLine($"  {actor} roles=[{string.Join(",", roles)}] → ALLOWED (expectedOk={expectOk})");
        }
        catch (ToyUnauthorizedApprovalException ex)
        {
            Console.WriteLine($"  {actor} roles=[{string.Join(",", roles)}] → DENIED: {ex.Message} (expectedOk={expectOk})");
        }
    }

    // =================================================================================
    // PART C  IDEMPOTENCY & CORRELATION
    // =================================================================================
    private static void PartC_IdempotencyAndCorrelation()
    {
        Header("PART C  REQUEST SAFETY — IDEMPOTENCY & CORRELATION");
        C1_IdempotencyKey();
        C2_CorrelationWithoutPii();
    }

    private static void C1_IdempotencyKey()
    {
        Section("C1  Idempotency-Key on selective retry");

        var store = new ToyApiIdempotencyStore();
        const string key = "retry-ops-7f3c";

        var first = store.Execute(key, () => new { requestId = "R1", status = "Queued" });
        var second = store.Execute(key, () => new { requestId = "R1-SHOULD-NOT-RUN", status = "Queued" });

        Console.WriteLine($"  first : {JsonSerializer.Serialize(first.Body)} replay={first.Replayed}");
        Console.WriteLine($"  second: {JsonSerializer.Serialize(second.Body)} replay={second.Replayed}");
        Console.WriteLine("  REAL: LicenseExtensionRetryEndpoints.IdempotencyKeyHeader = \"Idempotency-Key\"");
        Console.WriteLine("  Missing key → 400. Concurrent overlapping items → retry_item_claims → 409.");
        Console.WriteLine("  Cite: docs/idempotency.md, EfRetryClaimStore, ApiIdempotencyRecord");
    }

    private static void C2_CorrelationWithoutPii()
    {
        Section("C2  Correlation logging without leaking PII");

        var rawEmail = "renewal.manager@entitlex.local";
        var correlationId = "c7e2b1a0-1111-2222-3333-444444444444";
        var customerName = "Acme Corp Legal";

        Console.WriteLine("  GOOD log fields:");
        Console.WriteLine($"    CorrelationId={correlationId} RequestId=... Provider=CloudShield Outcome=Applied");
        Console.WriteLine("  BAD log fields:");
        Console.WriteLine($"    email={rawEmail} customerName={customerName} rawAuthorization=Bearer ...");
        Console.WriteLine();
        Console.WriteLine($"  Redacted email demo: {Pii.RedactEmail(rawEmail)}");
        Console.WriteLine("  Middleware: CorrelationIdMiddleware pushes Serilog property CorrelationId.");
        Console.WriteLine("  Metrics: never label by requestId/messageId/logicalOperationId (cardinality).");
        Console.WriteLine("  Cite: docs/observability.md, interview-guide §11");
    }

    // =================================================================================
    // PART D  SECRETS, HTTPS, CORS
    // =================================================================================
    private static void PartD_SecretsHttpsCors()
    {
        Header("PART D  SECRETS, TRANSPORT, CORS");
        D1_SecretsHygiene();
        D2_HttpsAndCors();
    }

    private static void D1_SecretsHygiene()
    {
        Section("D1  Secrets never in terraform state intent / git");

        var scanner = new ToySecretScanner();
        scanner.AddFile("infra/terraform/terraform.tfvars", "environment=\"lab\"\n");
        scanner.AddFile("infra/terraform/secrets.auto.tfvars", "jwt_signing_key=\"super-secret-should-not-be-here-32\"\n");
        scanner.AddFile("src/EntitleX.Api/appsettings.Development.json", "{ \"Jwt\": { \"SigningKey\": \"local-only-key-32-chars-minimum!!\" } }\n");

        foreach (var finding in scanner.Scan())
            Console.WriteLine($"  FINDING: {finding}");

        Console.WriteLine();
        Console.WriteLine("  Correct AWS path:");
        Console.WriteLine("    modules/secrets creates empty shells (jwt/signing-key, connection-strings, kafka)");
        Console.WriteLine("    aws secretsmanager put-secret-value  # or CI OIDC");
        Console.WriteLine("    ECS task secrets inject Jwt__SigningKey at runtime");
        Console.WriteLine("  NEVER: AWS access keys in images; commit tfvars with secrets; paste keys in chat.");
    }

    private static void D2_HttpsAndCors()
    {
        Section("D2  HTTPS metadata + CORS notes");

        Console.WriteLine("  JwtBearerOptions.RequireHttpsMetadata = true");
        Console.WriteLine("    → metadata/authority over HTTPS in non-dev deployments.");
        Console.WriteLine("  Local HTTP (:5080) is a Development convenience behind loopback.");
        Console.WriteLine();
        Console.WriteLine("  CORS (portfolio guidance):");
        Console.WriteLine("    • Vite dev server proxies /api → API (same-origin from browser's view)");
        Console.WriteLine("    • If enabling browser CORS: explicit origins, no AllowAnyOrigin+credentials");
        Console.WriteLine("    • nginx frontend image serves SPA; API separate — configure origins carefully");
        Console.WriteLine("  CSRF: Bearer tokens in Authorization header (not cookie session) reduce CSRF surface.");
    }

    // =================================================================================
    // PART E  PROVIDER & SIMULATOR TRUST
    // =================================================================================
    private static void PartE_ProviderAndSimulators()
    {
        Header("PART E  PROVIDER & SIMULATOR TRUST BOUNDARIES");
        E1_PostNoRetry();
        E2_SimulatorTrust();
    }

    private static void E1_PostNoRetry()
    {
        Section("E1  Provider POST no-retry (unsafe to blindly re-POST)");

        var getClient = new ToyHttpPipeline(retryOnGet: true, retryOnPost: false);
        var commandClient = new ToyHttpPipeline(retryOnGet: false, retryOnPost: false);

        Console.WriteLine($"  GET pipeline retries GET? {getClient.WouldRetry("GET")} POST? {getClient.WouldRetry("POST")}");
        Console.WriteLine($"  CMD pipeline retries GET? {commandClient.WouldRetry("GET")} POST? {commandClient.WouldRetry("POST")}");

        Console.WriteLine("  REAL: ProviderHttpClientRegistration");
        Console.WriteLine("    AddIdempotentGetClient — retry + CB + bulkhead, ShouldHandle only HttpMethod.Get");
        Console.WriteLine("    AddCommandClient — timeout + CB + bulkhead, NO retry strategy");
        Console.WriteLine("  Uncertain POSTs → ReconciliationWorker GET by LogicalOperationId — never blind re-POST.");
        Console.WriteLine("  Cite: docs/reconciliation.md, interview-guide §7");
    }

    private static void E2_SimulatorTrust()
    {
        Section("E2  Simulator trust boundaries");

        Console.WriteLine("  CloudShield / NetworkGuard are fictional vendors in-process/lab:");
        Console.WriteLine("    • Own PostgreSQL DBs (cloudshield / networkguard)");
        Console.WriteLine("    • Unique AppliedLicenseOperation(logical_operation_id) claims");
        Console.WriteLine("    • Consume their Kafka command topics; publish results");
        Console.WriteLine();
        Console.WriteLine("  Trust model for the portfolio:");
        Console.WriteLine("    • Treat simulator HTTP/Kafka as UNTRUSTED I/O — validate payloads");
        Console.WriteLine("    • Do not reuse simulator signing keys / DB creds for platform");
        Console.WriteLine("    • PlatformAdmin recon can probe sims; still no silent POST");
        Console.WriteLine("    • In a real vendor integration: mTLS/API keys, allowlists, egress SG rules");
        Console.WriteLine("  ECS: simulators are separate services with same app SG (lab) — tighten in prod.");
    }

    // =================================================================================
    // PART F  OWASP-ISH CHECKLIST → ENTITLEX
    // =================================================================================
    private static void PartF_OwaspChecklist()
    {
        Header("PART F  OWASP-ISH CHECKLIST MAPPED TO ENTITLEX");

        var rows = new (string Id, string Risk, string EntitleXControl)[]
        {
            ("API1", "BOLA / IDOR", "Policies + domain ownership checks; recon admin-only"),
            ("API2", "Broken authn", "JWT validation (iss/aud/exp/sig); SigningKey ≥32; HTTPS metadata"),
            ("API3", "Property-level authz", "Decision DTOs limited; mass-assignment avoided via explicit records"),
            ("API4", "Resource consumption", "Polly bulkheads/timeouts; outbox batch sizes; lambda bounded scans"),
            ("API5", "Function-level authz", "CanManageReconciliation PlatformAdmin-only; role policies on endpoints"),
            ("API7", "SSRF", "Provider BaseUrl from config allowlist — not from request body"),
            ("API8", "Security misconfig", "Dev token flag; secrets outside TF; SQS DenyInsecureTransport"),
            ("API9", "Inventory", "Versioned MessageEnvelope; documented endpoints in api-contracts.md"),
            ("API10", "Unsafe API consumption", "No POST retry; recon before retry; validate simulator results"),
            ("A02", "Crypto failures", "HS256 with strong key; SM for secrets; S3 SSE-KMS audit bucket"),
            ("A09", "Logging failures", "Serilog + CorrelationId; avoid PII in logs/metrics labels"),
        };

        Console.WriteLine("  ID     Risk                         EntitleX control");
        Console.WriteLine("  ------ ---------------------------- ------------------------------------------------");
        foreach (var r in rows)
            Console.WriteLine($"  {r.Id,-6} {r.Risk,-28} {r.EntitleXControl}");
    }

    // =================================================================================
    // PART G  INTERVIEW Q&A
    // =================================================================================
    private static void PartG_InterviewQa()
    {
        Header("PART G  INTERVIEW Q&A");

        Qa("Why HS256 instead of RSA/OIDC for EntitleX?",
            "Portfolio simplicity — single service trusts one signing key in SM. Call out OIDC as next step.");

        Qa("How do you stop a Renewal Rep from approving?",
            "CanDecideApprovals excludes Rep; ApprovalCase.RecordDecision throws UnauthorizedApprovalTransitionException.");

        Qa("Why is POST retry dangerous?",
            "Extension may have applied while client saw timeout; re-POST could double-extend — recon GET first.");

        Qa("Where do production secrets live?",
            "Secrets Manager shells via Terraform; values via CLI/OIDC; injected into ECS task — never git/tfvars.");

        Qa("What is defense in depth here?",
            "JWT authn → policy authz → domain role check → provider idempotency → recon gate → IAM least privilege.");

        Console.WriteLine("  MEMORY AID");
        Console.WriteLine("    Authn: JWT HS256 ≥32, iss/aud/exp");
        Console.WriteLine("    Authz: policies + ApprovalCase + admin recon");
        Console.WriteLine("    Safety: Idempotency-Key, no POST retry, outbox, processed_messages");
        Console.WriteLine("    Hygiene: secrets outside TF/git, redact PII, correlation ids");
        Console.WriteLine("    Boundary: simulators untrusted I/O; unique logical ops");
    }

    // ---------------------------------------------------------------------------------
    // Helpers + inline types
    // ---------------------------------------------------------------------------------
    private static void Header(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 78));
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"-- {title}");
    }

    private static void Qa(string q, string a)
    {
        Console.WriteLine($"  Q: {q}");
        Console.WriteLine($"  A: {a}");
        Console.WriteLine();
    }

    private static void Reject(string name, Action action)
    {
        try
        {
            action();
            Console.WriteLine($"  {name}: UNEXPECTED ACCEPT");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {name}: rejected ({ex.GetType().Name})");
        }
    }

    private static class ToyRoles
    {
        public const string RenewalRepresentative = "RenewalRepresentative";
        public const string RenewalManager = "RenewalManager";
        public const string DealDeskManager = "DealDeskManager";
        public const string PlatformAdmin = "PlatformAdmin";
    }

    private static class ToyJwt
    {
        public static string Create(
            string signingKey,
            string userId,
            string email,
            IEnumerable<string> roles,
            string issuer,
            string audience,
            int lifetimeMinutes)
        {
            var header = B64(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" }));
            var now = DateTimeOffset.UtcNow;
            var payload = B64(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["sub"] = userId,
                ["email"] = email,
                ["iss"] = issuer,
                ["aud"] = audience,
                ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
                ["exp"] = now.AddMinutes(lifetimeMinutes).ToUnixTimeSeconds(),
                ["roles"] = roles.ToArray()
            }));
            var sig = Sign($"{header}.{payload}", signingKey);
            return $"{header}.{payload}.{sig}";
        }

        public static string ForgeAlgNone(string userId, IEnumerable<string> roles)
        {
            var header = B64(JsonSerializer.Serialize(new { alg = "none", typ = "JWT" }));
            var payload = B64(JsonSerializer.Serialize(new { sub = userId, roles = roles.ToArray(), exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds() }));
            return $"{header}.{payload}.";
        }

        public static ToyPrincipal Validate(string token, string signingKey, string issuer, string audience)
        {
            var parts = token.Split('.');
            if (parts.Length != 3) throw new InvalidOperationException("malformed");
            var header = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FromB64(parts[0]))
                ?? throw new InvalidOperationException("bad header");
            if (!header.TryGetValue("alg", out var alg) || alg.GetString() != "HS256")
                throw new SecurityException("alg not allowed");
            var expected = Sign($"{parts[0]}.{parts[1]}", signingKey);
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected),
                    Encoding.UTF8.GetBytes(parts[2])))
                throw new SecurityException("bad signature");

            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(FromB64(parts[1]))
                ?? throw new InvalidOperationException("bad payload");
            if (payload["iss"].GetString() != issuer) throw new SecurityException("bad iss");
            if (payload["aud"].GetString() != audience) throw new SecurityException("bad aud");
            var exp = payload["exp"].GetInt64();
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) throw new SecurityException("expired");

            var roles = payload["roles"].EnumerateArray().Select(x => x.GetString()!).ToArray();
            return new ToyPrincipal(payload["sub"].GetString()!, roles);
        }

        private static string Sign(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            return Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
        }

        private static string B64(string json) => Base64Url(Encoding.UTF8.GetBytes(json));
        private static string FromB64(string s) => Encoding.UTF8.GetString(FromBase64Url(s));

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] FromBase64Url(string s)
        {
            s = s.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            return Convert.FromBase64String(s);
        }
    }

    private sealed record ToyPrincipal(string UserId, string[] Roles);

    private sealed class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
    }

    private sealed class ToyUnauthorizedApprovalException : Exception
    {
        public ToyUnauthorizedApprovalException(string message) : base(message) { }
    }

    private sealed class ToyApprovalCase
    {
        private readonly string _requiredRole;
        public bool Decided { get; private set; }
        private ToyApprovalCase(string requiredRole) => _requiredRole = requiredRole;
        public static ToyApprovalCase CreatePending(string requiredRole) => new(requiredRole);

        public void RecordDecision(string actorUserId, IEnumerable<string> actorRoles)
        {
            var roles = actorRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!roles.Contains(_requiredRole) && !roles.Contains(ToyRoles.PlatformAdmin))
            {
                throw new ToyUnauthorizedApprovalException(
                    $"Actor roles [{string.Join(", ", roles)}] cannot decide stage requiring '{_requiredRole}'.");
            }
            Decided = true;
            _ = actorUserId;
        }
    }

    private sealed class PolicyGate
    {
        private readonly Dictionary<string, HashSet<string>> _map = new()
        {
            ["CanManageReconciliation"] = new HashSet<string> { ToyRoles.PlatformAdmin },
            ["CanCreateRequests"] = new HashSet<string> { ToyRoles.RenewalRepresentative, ToyRoles.PlatformAdmin },
        };

        public bool Allows(string role, string policy) =>
            _map.TryGetValue(policy, out var roles) && roles.Contains(role);
    }

    private sealed class ToyApiIdempotencyStore
    {
        private readonly Dictionary<string, object> _store = new();

        public (object Body, bool Replayed) Execute(string key, Func<object> action)
        {
            if (_store.TryGetValue(key, out var existing))
                return (existing, true);
            var body = action();
            _store[key] = body;
            return (body, false);
        }
    }

    private static class Pii
    {
        public static string RedactEmail(string email)
        {
            var at = email.IndexOf('@');
            if (at <= 1) return "***";
            return email[0] + "***" + email[at..];
        }
    }

    private sealed class ToySecretScanner
    {
        private readonly List<(string Path, string Content)> _files = new();
        public void AddFile(string path, string content) => _files.Add((path, content));

        public IEnumerable<string> Scan()
        {
            foreach (var (path, content) in _files)
            {
                if (path.EndsWith(".tfvars", StringComparison.OrdinalIgnoreCase)
                    && content.Contains("signing_key", StringComparison.OrdinalIgnoreCase)
                    && content.Contains('"'))
                {
                    yield return $"{path}: possible secret in tfvars (should be SM put-secret-value)";
                }
            }
        }
    }

    private sealed class ToyHttpPipeline
    {
        private readonly bool _retryOnGet;
        private readonly bool _retryOnPost;
        public ToyHttpPipeline(bool retryOnGet, bool retryOnPost)
        {
            _retryOnGet = retryOnGet;
            _retryOnPost = retryOnPost;
        }

        public bool WouldRetry(string method) => method.ToUpperInvariant() switch
        {
            "GET" => _retryOnGet,
            "POST" => _retryOnPost,
            _ => false
        };
    }
}
