// =====================================================================================
//  13_AwsBackendDeepDive.cs
//  EntitleX-grounded AWS backend deep dive — VPC, RDS, ECS Fargate, SQS/DLQ, Lambda,
//  EventBridge, Secrets Manager, CloudWatch, S3 audit, IAM least privilege, LocalStack,
//  and cost awareness (infra/terraform/COST.md).
//
//  HOW TO RUN (needs .NET 8 SDK, NO NuGet packages):
//      dotnet new console -n AwsBackendDeepDive && cd AwsBackendDeepDive
//      replace Program.cs with this file, then:  dotnet run
//
//  HOW TO READ THIS FILE
//    Every section: CONCEPT / WHY / TRADE-OFF / DEMO (in-memory simulation).
//    Real paths cite EntitleX under infra/terraform/ and src/lambdas/.
//    Demos are simulations — not live AWS calls. Real Terraform/SDK snippets live in comments.
//
//  TABLE OF CONTENTS
//   PART A  Footprint & cost mindset
//     A0  Compose-primary vs optional Terraform packaging
//     A1  COST.md drivers (NAT, RDS, Fargate, Secrets)
//   PART B  Networking & data plane
//     B1  VPC modules (public/private, security groups)
//     B2  RDS PostgreSQL + Secrets Manager master password
//     B3  Availability: single-AZ lab vs Multi-AZ
//   PART C  Compute
//     C1  ECS Fargate services (API / workers / simulators)
//     C2  Task role vs execution role
//     C3  Lambda + EventBridge (ApprovalReminder, ExpirationNotification)
//     C4  Bounded work in scheduled lambdas
//   PART D  Messaging & async
//     D1  SQS fulfillment + DLQ (maxReceiveCount 5)
//     D2  Outbox relay → SQS (why dual-write is avoided)
//     D3  LocalStack for local SQS/SES
//   PART E  Secrets, audit, observability, IAM
//     E1  Secrets Manager shells (JWT / connection strings outside TF)
//     E2  S3 audit exports (SSE-KMS, TLS-only)
//     E3  CloudWatch log groups, alarms, dashboard
//     E4  IAM least privilege simulation
//   PART F  Interview Q&A + memory aid
// =====================================================================================

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EntitleX.Guides.AwsBackendDeepDive;

public static class Program
{
    public static async Task Main()
    {
        PartA_FootprintAndCost();
        PartB_NetworkingAndData();
        await PartC_ComputeAsync();
        await PartD_MessagingAsync();
        PartE_SecretsAuditObservabilityIam();
        PartF_InterviewQa();

        Console.WriteLine();
        Console.WriteLine("All EntitleX AWS deep-dive demos finished.");
    }

    // =================================================================================
    // PART A  FOOTPRINT & COST MINDSET
    // =================================================================================
    //
    // A0. COMPOSE-PRIMARY VS OPTIONAL TERRAFORM
    //
    // CONCEPT
    //   EntitleX's day-to-day path is Docker Compose (Postgres, Kafka KRaft, LocalStack,
    //   Mailpit, Jaeger, Prometheus, Grafana) — see docker-compose.yml and docs/deployment.md.
    //   AWS packaging under infra/terraform/ is OPTIONAL lab/staging packaging, gated by
    //   flags like enable_ecs_services and enable_lambda_schedules.
    //
    // WHY
    //   Portfolio iteration speed and cost. terraform apply spends real money (NAT, RDS,
    //   Fargate). Interviewers love: "packaging ≠ production readiness."
    //
    // TRADE-OFF
    //   Compose does not prove IAM/Secrets Manager/ALB/DNS/MSK. Terraform proves packaging
    //   shape, not a hardened production deploy. Kafka/MSK is intentionally NOT provisioned
    //   (bring bootstrap via Secrets Manager entitlex/kafka).
    //
    // REAL PATHS
    //   infra/terraform/main.tf          — wires modules + app security group
    //   infra/terraform/README.md        — module table
    //   infra/terraform/COST.md          — cost drivers + destroy guidance
    //   docs/deployment.md               — Compose vs AWS matrix
    // =================================================================================
    private static void PartA_FootprintAndCost()
    {
        Header("PART A  FOOTPRINT & COST MINDSET");
        A0_ComposeVsTerraform();
        A1_CostDrivers();
    }

    private static void A0_ComposeVsTerraform()
    {
        Section("A0  Compose-primary vs optional Terraform packaging");

        var footprints = new[]
        {
            new FootprintRow("Postgres", "Compose: postgres:16 :5432", "AWS: RDS modules/rds (encrypted)"),
            new FootprintRow("Kafka", "Compose: apache/kafka:3.8.0 KRaft :9092", "AWS: NOT in Terraform — secret entitlex/kafka"),
            new FootprintRow("SQS/SES", "LocalStack :4566 (init-sqs.sh)", "Real SQS + SES modules"),
            new FootprintRow("Apps", "dotnet run API/workers/sims", "ECS Fargate when enable_ecs_services=true"),
            new FootprintRow("Lambdas", "dotnet run ApprovalReminder/Expiration", "Lambda + EventBridge when gated on"),
            new FootprintRow("Obs", "Jaeger/Prometheus/Grafana", "CloudWatch logs/alarms (+ optional OTel)"),
        };

        Console.WriteLine("  Concern          | Local Compose                         | Optional AWS");
        Console.WriteLine("  -----------------|---------------------------------------|--------------------------------");
        foreach (var r in footprints)
        {
            Console.WriteLine($"  {r.Concern,-16} | {r.Compose,-37} | {r.Aws}");
        }

        Console.WriteLine();
        Console.WriteLine("  Rule: destroy idle labs (terraform destroy). Prefer Compose daily.");
        Console.WriteLine("  Cite: infra/terraform/COST.md + docs/deployment.md");
    }

    private static void A1_CostDrivers()
    {
        Section("A1  COST.md — what actually burns money");

        // DEMO: estimate monthly lab cost from toggles (toy numbers from COST.md guidance).
        var scenarios = new[]
        {
            new CostScenario("defaults (ECS off, Lambda off, NAT on, single-AZ micro)",
                nat: true, ecsTasks: 0, rdsMultiAz: false, secrets: 4),
            new CostScenario("cheapest lab (no NAT, everything gated off)",
                nat: false, ecsTasks: 0, rdsMultiAz: false, secrets: 3),
            new CostScenario("ECS on (1 API 0.5vCPU + 4 workers + 2 sims) with NAT",
                nat: true, ecsTasks: 7, rdsMultiAz: false, secrets: 4),
        };

        foreach (var s in scenarios)
        {
            var estimate = s.EstimateUsdPerMonth();
            Console.WriteLine($"  {s.Name}");
            Console.WriteLine($"    ≈ ${estimate:0} / month idle (toy model from COST.md drivers)");
        }

        Console.WriteLine();
        Console.WriteLine("  COST CHECKLIST (from infra/terraform/COST.md):");
        Console.WriteLine("    1. environment=lab → skip_final_snapshot, no deletion protection");
        Console.WriteLine("    2. enable_ecs_services=false until images exist");
        Console.WriteLine("    3. rds_multi_az=false unless testing HA");
        Console.WriteLine("    4. Tear down NAT when private egress not needed");
        Console.WriteLine("    5. CloudWatch retention 14 days in lab");
        Console.WriteLine("  NAT Gateway is the LARGEST idle cost (~$32/mo + data).");
    }

    // =================================================================================
    // PART B  NETWORKING & DATA PLANE
    // =================================================================================
    //
    // B1. VPC
    //   modules/vpc: public + private subnets across availability_zones, optional NAT.
    //   main.tf creates aws_security_group.app before RDS/ECS to avoid cycles:
    //     ingress TCP 8080 from vpc_cidr (HTTP within VPC)
    //     egress all (lab simplicity — tighten in real prod)
    //
    // B2. RDS
    //   modules/rds: encrypted Postgres, private subnets, SG allows app SG only.
    //   Master password: random_password → Secrets Manager (never in tfvars/git).
    //
    // B3. AVAILABILITY
    //   Lab default: single-AZ db.t4g.micro. Multi-AZ roughly doubles RDS cost.
    //   Fargate tasks in private subnets (assign_public_ip=false) need NAT for ECR pull
    //   unless you use VPC endpoints — another cost/ops trade-off.
    // =================================================================================
    private static void PartB_NetworkingAndData()
    {
        Header("PART B  NETWORKING & DATA PLANE");
        B1_VpcAndSecurityGroups();
        B2_RdsAndMasterSecret();
        B3_Availability();
    }

    private static void B1_VpcAndSecurityGroups()
    {
        Section("B1  VPC + security groups (EntitleX lab shape)");

        var vpc = new ToyVpc("10.20.0.0/16", azs: new[] { "a", "b" });
        vpc.AddSubnet("public-a", "10.20.0.0/24", isPublic: true);
        vpc.AddSubnet("public-b", "10.20.1.0/24", isPublic: true);
        vpc.AddSubnet("private-a", "10.20.10.0/24", isPublic: false);
        vpc.AddSubnet("private-b", "10.20.11.0/24", isPublic: false);

        var appSg = new ToySecurityGroup("entitlex-app", vpc.Cidr);
        appSg.AllowIngress(8080, vpc.Cidr, "HTTP within VPC — ECS API/sims");
        appSg.AllowEgressAll("lab simplicity");

        var rdsSg = new ToySecurityGroup("entitlex-rds", vpc.Cidr);
        rdsSg.AllowIngressFromSg(5432, appSg.Name, "Postgres only from app SG");

        Console.WriteLine($"  VPC {vpc.Cidr} AZs={string.Join(",", vpc.Azs)}");
        foreach (var s in vpc.Subnets)
            Console.WriteLine($"    subnet {s.Name,-12} {s.Cidr,-15} public={s.IsPublic}");

        Console.WriteLine($"  SG {appSg.Name}: {appSg.Describe()}");
        Console.WriteLine($"  SG {rdsSg.Name}: {rdsSg.Describe()}");
        Console.WriteLine();
        Console.WriteLine("  Cite: infra/terraform/modules/vpc/, main.tf aws_security_group.app");
        Console.WriteLine("  WHY private Fargate? Reduce internet exposure; secrets stay in VPC.");
        Console.WriteLine("  TRADE-OFF: private tasks need NAT or VPC endpoints to pull ECR images.");
    }

    private static void B2_RdsAndMasterSecret()
    {
        Section("B2  RDS Postgres + master password never in Terraform state as plaintext intent");

        // DEMO: simulate "random_password → Secrets Manager" without putting the secret in tfvars.
        var generated = ToySecrets.GeneratePassword(24);
        var secretArn = ToySecrets.PutSecret("entitlex/rds/master", generated);
        var retrieved = ToySecrets.GetSecret(secretArn);

        Console.WriteLine($"  Generated master password length={generated.Length} (not printed)");
        Console.WriteLine($"  Stored under ARN-like handle: {secretArn}");
        Console.WriteLine($"  Retrieve works: match={retrieved == generated}");
        Console.WriteLine();
        Console.WriteLine("  REAL: modules/rds creates encrypted instance; password → SM.");
        Console.WriteLine("  Connection strings composed AFTER DBs exist → entitlex/connection-strings");
        Console.WriteLine("  JSON keys like EntitleXPlatform (see modules/secrets + ECS task secrets).");
        Console.WriteLine("  NEVER: hardcode passwords in .tf / .tfvars / Docker images.");
    }

    private static void B3_Availability()
    {
        Section("B3  Availability choices for a portfolio lab");

        var singleAz = new AvailabilityChoice("single-AZ db.t4g.micro", MultiAz: false, MonthlyUsd: 14);
        var multiAz = new AvailabilityChoice("Multi-AZ", MultiAz: true, MonthlyUsd: 28);

        Console.WriteLine($"  {singleAz.Name}: multiAz={singleAz.MultiAz} ≈${singleAz.MonthlyUsd}/mo");
        Console.WriteLine($"  {multiAz.Name}: multiAz={multiAz.MultiAz} ≈${multiAz.MonthlyUsd}/mo");
        Console.WriteLine();
        Console.WriteLine("  Interview answer: lab proves packaging with single-AZ; Multi-AZ is");
        Console.WriteLine("  a deliberate HA spend. Fargate desired_count + multi-AZ subnets give");
        Console.WriteLine("  compute AZ spread without doubling RDS until you need it.");
        Console.WriteLine("  Cite: variables rds_multi_az, COST.md Multi-AZ note.");
    }

    // =================================================================================
    // PART C  COMPUTE — ECS FARGATE + LAMBDA
    // =================================================================================
    //
    // C1. ECS SERVICES (main.tf locals.ecs_services when enable_ecs_services)
    //   api (512/1024, port 8080), outbox, fulfillment, reconciliation, notification,
    //   cloudshield, networkguard. Images from container_image_* variables.
    //
    // C2. TASK ROLE vs EXECUTION ROLE (modules/iam)
    //   Execution: pull ECR, write logs, GetSecretValue for injection.
    //   Task: what APP code can do — SQS, SES, S3 audit, secrets read, PutMetricData.
    //
    // C3. LAMBDAS
    //   src/lambdas/EntitleX.ApprovalReminder — rate(1 hour)
    //   src/lambdas/EntitleX.ExpirationNotification — CloudShield daily / NetworkGuard weekly
    //   EventBridge mirrors infra/eventbridge/schedules.stub.json
    //
    // C4. BOUNDED WORK
    //   Lambdas scan candidates with idempotent skips (SkippedIdempotent). Timeouts
    //   (60s reminder, 120s expiration) force bounded batches — never unbounded scans.
    // =================================================================================
    private static async Task PartC_ComputeAsync()
    {
        Header("PART C  COMPUTE — ECS FARGATE + LAMBDA");
        C1_EcsFargateServices();
        C2_TaskVsExecutionRole();
        await C3_LambdaEventBridgeAsync();
        await C4_BoundedLambdaWorkAsync();
    }

    private static void C1_EcsFargateServices()
    {
        Section("C1  ECS Fargate service map (EntitleX)");

        var services = new[]
        {
            new EcsServiceSpec("api", 512, 1024, Port: 8080, Role: "HTTP control plane"),
            new EcsServiceSpec("outbox", 256, 512, Port: null, Role: "OutboxPublisher → SQS"),
            new EcsServiceSpec("fulfillment", 512, 1024, Port: null, Role: "SQS → Kafka fan-out"),
            new EcsServiceSpec("reconciliation", 256, 512, Port: null, Role: "Probe/repair loop"),
            new EcsServiceSpec("notification", 256, 512, Port: null, Role: "Email worker"),
            new EcsServiceSpec("cloudshield", 256, 512, Port: 8080, Role: "Vendor simulator"),
            new EcsServiceSpec("networkguard", 256, 512, Port: 8080, Role: "Vendor simulator"),
        };

        Console.WriteLine("  Service           CPU  Mem   Port   Role");
        foreach (var s in services)
        {
            Console.WriteLine(
                $"  {s.Name,-16} {s.Cpu,4} {s.Memory,5}  {(s.Port?.ToString() ?? "-"),4}   {s.Role}");
        }

        Console.WriteLine();
        Console.WriteLine("  Task def injects env (ASPNETCORE_ENVIRONMENT, Email__*, Messaging__Sqs__*)");
        Console.WriteLine("  and secrets Jwt__SigningKey + ConnectionStrings__EntitleXPlatform.");
        Console.WriteLine("  Cite: infra/terraform/modules/ecs/main.tf, main.tf locals.ecs_services");
        Console.WriteLine("  Images: infra/docker/images/Dockerfile.{api,worker,simulator,frontend,lambda}");
    }

    private static void C2_TaskVsExecutionRole()
    {
        Section("C2  Task role vs execution role (interview classic)");

        var exec = new IamRole("entitlex-ecs-exec", "ecs-tasks.amazonaws.com");
        exec.Allow("ecr:Get*", "logs:CreateLogStream", "logs:PutLogEvents", "secretsmanager:GetSecretValue");

        var task = new IamRole("entitlex-ecs-task", "ecs-tasks.amazonaws.com");
        task.Allow(
            "sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:SendMessage", "sqs:GetQueueUrl",
            "ses:SendEmail", "s3:PutObject", "secretsmanager:GetSecretValue", "cloudwatch:PutMetricData");

        Console.WriteLine($"  Execution role {exec.Name}: platform needs → {string.Join(", ", exec.Actions.Take(3))}...");
        Console.WriteLine($"  Task role {task.Name}: app needs → SQS/SES/S3/Secrets/CW metrics");
        Console.WriteLine();
        Console.WriteLine("  WHY split? Least privilege + clear blast radius. Compromised app");
        Console.WriteLine("  code should not need ECR pull rights; ECS agent should not SendEmail.");
        Console.WriteLine("  Cite: infra/terraform/modules/iam/main.tf");
    }

    private static async Task C3_LambdaEventBridgeAsync()
    {
        Section("C3  Lambda + EventBridge schedules");

        var bus = new ToyEventBridge();
        bus.AddRule("entitlex-approval-reminder", "rate(1 hour)", "ApprovalReminder", null);
        bus.AddRule("entitlex-expiration-cloudshield", "rate(24 hours)", "ExpirationNotification",
            new { providers = new[] { "CloudShield" } });
        bus.AddRule("entitlex-expiration-networkguard", "rate(7 days)", "ExpirationNotification",
            new { providers = new[] { "NetworkGuard" } });

        foreach (var rule in bus.Rules)
        {
            var input = rule.Input is null ? "(none)" : JsonSerializer.Serialize(rule.Input);
            Console.WriteLine($"  {rule.Name}: {rule.Schedule} → {rule.Target} input={input}");
        }

        // DEMO: invoke handlers the way local hosts / Lambda would.
        var reminder = await ToyLambda.RunApprovalReminderAsync();
        var expiration = await ToyLambda.RunExpirationAsync(new[] { "CloudShield" });

        Console.WriteLine($"  ApprovalReminder demo: scanned={reminder.Scanned} reminders={reminder.Reminders} escalations={reminder.Escalations}");
        Console.WriteLine($"  ExpirationNotification demo: scanned={expiration.Scanned} sent={expiration.Sent}");
        Console.WriteLine();
        Console.WriteLine("  REAL handlers:");
        Console.WriteLine("    src/lambdas/EntitleX.ApprovalReminder/Function.cs → IApprovalReminderService");
        Console.WriteLine("    src/lambdas/EntitleX.ExpirationNotification/Function.cs → IExpirationNotificationService");
        Console.WriteLine("  Terraform: modules/lambda + modules/eventbridge");
        Console.WriteLine("  Stub: infra/eventbridge/schedules.stub.json");
    }

    private static async Task C4_BoundedLambdaWorkAsync()
    {
        Section("C4  Bounded work — lambdas must not scan forever");

        // DEMO: process a large candidate set in chunks within a timeout budget.
        var candidates = Enumerable.Range(1, 250).Select(i => $"case-{i:D3}").ToList();
        var budget = TimeSpan.FromMilliseconds(50);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var processed = 0;
        var skipped = 0;
        var seen = new HashSet<string> { "case-001", "case-002" }; // already reminded (idempotent)

        foreach (var batch in candidates.Chunk(25))
        {
            if (sw.Elapsed >= budget)
            {
                Console.WriteLine($"  Budget exhausted after {processed} processed / {skipped} skipped — stop (next schedule continues).");
                break;
            }

            foreach (var id in batch)
            {
                if (!seen.Add(id)) { skipped++; continue; }
                processed++;
                await Task.Yield();
            }
        }

        Console.WriteLine($"  Result: processed={processed} skippedIdempotent={skipped} remaining≈{candidates.Count - processed - skipped}");
        Console.WriteLine("  WHY: EventBridge will fire again; idempotent skips make overlap safe.");
        Console.WriteLine("  REAL: ApprovalReminderRunResult.SkippedIdempotent / ExpirationNotificationRunResult");
    }

    // =================================================================================
    // PART D  MESSAGING & ASYNC
    // =================================================================================
    //
    // D1. SQS + DLQ
    //   modules/sqs: fulfillment queue + DLQ, maxReceiveCount=5, SSE, DenyInsecureTransport.
    //   Local: infra/docker/localstack/init-sqs.sh creates entitlex-fulfillment (+ DLQ).
    //
    // D2. OUTBOX RELAY
    //   Final approval writes outbox_messages in SAME Postgres transaction as domain updates.
    //   OutboxPublisher claims FOR UPDATE SKIP LOCKED → SQS. API never holds DB txn across SQS.
    //
    // D3. LOCALSTACK
    //   SERVICES includes s3,sqs,sns,ses,lambda,secretsmanager,... so local workers use
    //   Messaging:Sqs:ServiceUrl=http://localhost:4566 with test/test keys.
    // =================================================================================
    private static async Task PartD_MessagingAsync()
    {
        Header("PART D  MESSAGING & ASYNC");
        await D1_SqsAndDlqAsync();
        await D2_OutboxRelayAsync();
        D3_LocalStack();
    }

    private static async Task D1_SqsAndDlqAsync()
    {
        Section("D1  SQS fulfillment + DLQ (maxReceiveCount 5)");

        var q = new ToySqsQueue("entitlex-fulfillment", maxReceiveCount: 5);
        await q.SendAsync("msg-1", "{ \"schemaVersion\":\"1\", \"messageType\":\"entitlex.fulfillment.command\" }");

        for (var i = 1; i <= 6; i++)
        {
            var msg = await q.ReceiveAsync();
            if (msg is null) break;
            Console.WriteLine($"  receive#{i}: id={msg.Id} receiveCount={msg.ReceiveCount} onDlq={msg.OnDlq}");
            if (i < 5)
            {
                // Simulate processing failure → visibility timeout expires → redelivery
                q.Fail(msg);
            }
            else if (!msg.OnDlq)
            {
                q.Fail(msg); // 5th failure → DLQ on next receive attempt path
            }
        }

        Console.WriteLine($"  Main depth={q.MainDepth} DLQ depth={q.DlqDepth}");
        Console.WriteLine("  Cite: infra/terraform/modules/sqs/main.tf, init-sqs.sh, MessagingOptions.Sqs");
        Console.WriteLine("  Consumer: src/EntitleX.Messaging/Sqs/SqsFulfillmentConsumer.cs");
    }

    private static async Task D2_OutboxRelayAsync()
    {
        Section("D2  Outbox relay to SQS — dual-write avoided");

        var db = new ToyOutboxStore();
        var sqs = new ToySqsQueue("entitlex-fulfillment", maxReceiveCount: 5);

        // Same "unit of work": domain commit + outbox insert succeed together.
        await db.CommitApprovalWithOutboxAsync(
            requestId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            messageId: Guid.Parse("11111111-2222-3333-4444-555555555555"));

        // Separate process: OutboxPublisher
        var claimed = await db.ClaimPendingAsync(batchSize: 10);
        foreach (var row in claimed)
        {
            await sqs.SendAsync(row.MessageId.ToString("D"), row.Body);
            await db.MarkPublishedAsync(row.MessageId);
            Console.WriteLine($"  Published outbox {row.MessageId:D} → SQS (status=Published)");
        }

        Console.WriteLine();
        Console.WriteLine("  BAD (dual-write): UPDATE saga; then SQS.Send — crash between → lost or orphan.");
        Console.WriteLine("  GOOD: INSERT outbox_messages in same PG txn; publisher is at-least-once.");
        Console.WriteLine("  Cite: docs/event-contracts.md, EntitleX.OutboxPublisher, interview-guide §3");
    }

    private static void D3_LocalStack()
    {
        Section("D3  LocalStack stands in for SQS/SES (and friends)");

        Console.WriteLine("  docker-compose.yml service localstack:");
        Console.WriteLine("    image localstack/localstack:3.8  port 4566");
        Console.WriteLine("    SERVICES: s3,sqs,sns,ses,lambda,secretsmanager,sts,iam");
        Console.WriteLine("    init: infra/docker/localstack/init-sqs.sh → entitlex-fulfillment (+ DLQ)");
        Console.WriteLine();
        Console.WriteLine("  App config (MessagingOptions.Sqs defaults):");
        Console.WriteLine("    ServiceUrl=http://localhost:4566  Region=us-east-1");
        Console.WriteLine("    AccessKey/SecretKey=test/test (LocalStack only!)");
        Console.WriteLine("  On AWS: NO static keys — ECS task role / Lambda role.");
    }

    // =================================================================================
    // PART E  SECRETS, AUDIT, OBSERVABILITY, IAM
    // =================================================================================
    private static void PartE_SecretsAuditObservabilityIam()
    {
        Header("PART E  SECRETS, AUDIT, OBSERVABILITY, IAM");
        E1_SecretsManagerShells();
        E2_S3Audit();
        E3_CloudWatch();
        E4_IamLeastPrivilege();
    }

    private static void E1_SecretsManagerShells()
    {
        Section("E1  Secrets Manager — create shells in TF, put values outside");

        var shells = new[]
        {
            "entitlex/jwt/signing-key     → Jwt__SigningKey (≥32 chars)",
            "entitlex/connection-strings → JSON map ConnectionStrings__*",
            "entitlex/kafka              → Messaging__Kafka__BootstrapServers",
            "RDS master secret           → from modules/rds",
        };

        foreach (var s in shells) Console.WriteLine($"  {s}");

        Console.WriteLine();
        Console.WriteLine("  Workflow:");
        Console.WriteLine("    terraform apply  # creates empty/placeholder secrets");
        Console.WriteLine("    aws secretsmanager put-secret-value --secret-id ... --secret-string ...");
        Console.WriteLine("    # or CI OIDC — see .github/workflows/deploy-aws.yml");
        Console.WriteLine("  Cite: infra/terraform/modules/secrets/main.tf");
        Console.WriteLine("  NEVER commit JWT keys / passwords to git or terraform.tfvars.");
    }

    private static void E2_S3Audit()
    {
        Section("E2  S3 audit export bucket");

        var bucket = new ToyAuditBucket("entitlex-audit");
        bucket.EnableVersioning();
        bucket.EnableSseKms();
        bucket.DenyInsecureTransport();
        bucket.Put("exports/2026-11-08/audit.jsonl", "{ \"event\":\"ApprovalDecided\" }");

        Console.WriteLine($"  Bucket {bucket.Name}: objects={bucket.ObjectCount} versioned={bucket.Versioned} sse={bucket.SseKms} tlsOnly={bucket.TlsOnly}");
        Console.WriteLine("  Cite: infra/terraform/modules/s3_audit/");
        Console.WriteLine("  IAM task role allows s3:PutObject on audit bucket only (least privilege).");
    }

    private static void E3_CloudWatch()
    {
        Section("E3  CloudWatch — logs, SQS alarms, dashboard");

        var services = new[] { "api", "outbox", "fulfillment", "reconciliation", "notification", "cloudshield", "networkguard", "lambda" };
        foreach (var s in services)
            Console.WriteLine($"  log group /entitlex/lab/ecs/{s} retention=14d");

        Console.WriteLine("  Alarms: fulfillment queue depth / DLQ depth → SNS alarm topic");
        Console.WriteLine("  Cite: infra/terraform/modules/cloudwatch/, modules/messaging (SNS)");
        Console.WriteLine("  App metrics: workers expose :9180–9183; API /metrics (Compose Prometheus).");
    }

    private static void E4_IamLeastPrivilege()
    {
        Section("E4  IAM least-privilege evaluator (demo)");

        var statements = new List<PolicyStatement>
        {
            new(Effect.Allow, new[] { "sqs:SendMessage", "sqs:ReceiveMessage" },
                new[] { "arn:aws:sqs:us-east-1:123:entitlex-fulfillment" }),
            new(Effect.Allow, new[] { "s3:PutObject" },
                new[] { "arn:aws:s3:::entitlex-audit/*" }),
            new(Effect.Deny, new[] { "sqs:*" },
                new[] { "arn:aws:sqs:us-east-1:123:entitlex-fulfillment" },
                whenInsecureTransport: true),
        };

        Eval("sqs:SendMessage", "arn:aws:sqs:us-east-1:123:entitlex-fulfillment", secure: true, statements);
        Eval("sqs:SendMessage", "arn:aws:sqs:us-east-1:123:entitlex-fulfillment", secure: false, statements);
        Eval("s3:DeleteObject", "arn:aws:s3:::entitlex-audit/x", secure: true, statements);
        Eval("s3:PutObject", "arn:aws:s3:::entitlex-audit/x", secure: true, statements);

        Console.WriteLine();
        Console.WriteLine("  Policy evaluation order: implicit deny → Allow opens → explicit Deny wins.");
        Console.WriteLine("  Cite: modules/iam + SQS DenyInsecureTransport in modules/sqs.");
    }

    private static void Eval(string action, string resource, bool secure, List<PolicyStatement> statements)
    {
        var (allowed, reason) = PolicyEvaluator.Evaluate(statements, action, resource, secure);
        Console.WriteLine($"  {action} on {resource.Split(':').Last()} secure={secure} → {(allowed ? "ALLOW" : "DENY")} ({reason})");
    }

    // =================================================================================
    // PART F  INTERVIEW Q&A
    // =================================================================================
    private static void PartF_InterviewQa()
    {
        Header("PART F  INTERVIEW Q&A + MEMORY AID");

        Qa("Why is Compose primary if Terraform exists?",
            "Cost + iteration. COST.md: NAT/RDS/Fargate idle spend. Terraform = optional packaging.");

        Qa("Task role vs execution role?",
            "Execution: ECS agent (ECR, logs, secret injection). Task: app (SQS/SES/S3/SM/CW).");

        Qa("Where do JWT and connection strings live?",
            "Secrets Manager shells in TF; values via CLI/OIDC outside Terraform — never tfvars/git.");

        Qa("How does fulfillment leave Postgres safely?",
            "Transactional outbox in same UoW → OutboxPublisher → SQS (+ DLQ maxReceiveCount 5).");

        Qa("Why no MSK in Terraform?",
            "Cost/complexity. Local Kafka via Compose; AWS deploys bring bootstrap via entitlex/kafka.");

        Qa("How are lambdas scheduled?",
            "EventBridge rate rules mirroring schedules.stub.json; provider filter in input JSON.");

        Console.WriteLine();
        Console.WriteLine("  MEMORY AID — EntitleX AWS map");
        Console.WriteLine("    VPC/SG → RDS + ECS/Lambda in private subnets");
        Console.WriteLine("    Outbox → SQS(+DLQ) → workers → Kafka (external) → sims → results");
        Console.WriteLine("    Secrets Manager (JWT/CS/Kafka) + S3 audit + CloudWatch");
        Console.WriteLine("    IAM least privilege; LocalStack locally; destroy labs when idle");
    }

    // ---------------------------------------------------------------------------------
    // Helpers & inline demo types (standalone compilable — no NuGet)
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

    private sealed record FootprintRow(string Concern, string Compose, string Aws);

    private sealed class CostScenario
    {
        public string Name { get; }
        private readonly bool _nat;
        private readonly int _ecsTasks;
        private readonly bool _rdsMultiAz;
        private readonly int _secrets;

        public CostScenario(string name, bool nat, int ecsTasks, bool rdsMultiAz, int secrets)
        {
            Name = name; _nat = nat; _ecsTasks = ecsTasks; _rdsMultiAz = rdsMultiAz; _secrets = secrets;
        }

        public double EstimateUsdPerMonth()
        {
            double total = 0;
            if (_nat) total += 32;
            total += _rdsMultiAz ? 28 : 14;
            total += _ecsTasks * 12; // rough "left on" lab
            total += _secrets * 0.40;
            total += 3; // CW logs/alarms variable floor
            return total;
        }
    }

    private sealed class ToyVpc
    {
        public string Cidr { get; }
        public string[] Azs { get; }
        public List<ToySubnet> Subnets { get; } = new();
        public ToyVpc(string cidr, string[] azs) { Cidr = cidr; Azs = azs; }
        public void AddSubnet(string name, string cidr, bool isPublic) =>
            Subnets.Add(new ToySubnet(name, cidr, isPublic));
    }

    private sealed record ToySubnet(string Name, string Cidr, bool IsPublic);

    private sealed class ToySecurityGroup
    {
        public string Name { get; }
        private readonly string _vpcCidr;
        private readonly List<string> _rules = new();
        public ToySecurityGroup(string name, string vpcCidr) { Name = name; _vpcCidr = vpcCidr; }
        public void AllowIngress(int port, string cidr, string note) =>
            _rules.Add($"ingress tcp/{port} from {cidr} ({note})");
        public void AllowIngressFromSg(int port, string sg, string note) =>
            _rules.Add($"ingress tcp/{port} from SG:{sg} ({note})");
        public void AllowEgressAll(string note) => _rules.Add($"egress all ({note})");
        public string Describe() => string.Join("; ", _rules);
    }

    private static class ToySecrets
    {
        private static readonly Dictionary<string, string> Store = new();
        private static int _seq;

        public static string GeneratePassword(int length)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
            var rng = Random.Shared;
            return new string(Enumerable.Range(0, length).Select(_ => alphabet[rng.Next(alphabet.Length)]).ToArray());
        }

        public static string PutSecret(string name, string value)
        {
            var arn = $"arn:toy:secretsmanager:us-east-1:000:{name}:{++_seq}";
            Store[arn] = value;
            return arn;
        }

        public static string GetSecret(string arn) => Store[arn];
    }

    private sealed record AvailabilityChoice(string Name, bool MultiAz, int MonthlyUsd);

    private sealed record EcsServiceSpec(string Name, int Cpu, int Memory, int? Port, string Role);

    private sealed class IamRole
    {
        public string Name { get; }
        public string Trust { get; }
        public List<string> Actions { get; } = new();
        public IamRole(string name, string trust) { Name = name; Trust = trust; }
        public void Allow(params string[] actions) => Actions.AddRange(actions);
    }

    private sealed class ToyEventBridge
    {
        public List<EbRule> Rules { get; } = new();
        public void AddRule(string name, string schedule, string target, object? input) =>
            Rules.Add(new EbRule(name, schedule, target, input));
    }

    private sealed record EbRule(string Name, string Schedule, string Target, object? Input);

    private static class ToyLambda
    {
        public static Task<(int Scanned, int Reminders, int Escalations)> RunApprovalReminderAsync()
        {
            // Mirrors Function.HandlerAsync → IApprovalReminderService.RunAsync shape.
            return Task.FromResult((Scanned: 12, Reminders: 3, Escalations: 1));
        }

        public static Task<(int Scanned, int Sent)> RunExpirationAsync(IReadOnlyCollection<string> providers)
        {
            Console.WriteLine($"  Expiration filter providers=[{string.Join(",", providers)}]");
            return Task.FromResult((Scanned: 40, Sent: 5));
        }
    }

    private sealed class ToySqsQueue
    {
        private readonly Queue<ToySqsMessage> _main = new();
        private readonly Queue<ToySqsMessage> _dlq = new();
        private readonly int _maxReceiveCount;
        public string Name { get; }
        public int MainDepth => _main.Count;
        public int DlqDepth => _dlq.Count;

        public ToySqsQueue(string name, int maxReceiveCount)
        {
            Name = name;
            _maxReceiveCount = maxReceiveCount;
        }

        public Task SendAsync(string id, string body)
        {
            _main.Enqueue(new ToySqsMessage(id, body, 0, false));
            return Task.CompletedTask;
        }

        public Task<ToySqsMessage?> ReceiveAsync()
        {
            if (_main.Count == 0) return Task.FromResult<ToySqsMessage?>(null);
            var msg = _main.Dequeue();
            var next = msg with { ReceiveCount = msg.ReceiveCount + 1 };
            return Task.FromResult<ToySqsMessage?>(next);
        }

        public void Fail(ToySqsMessage msg)
        {
            if (msg.ReceiveCount >= _maxReceiveCount)
                _dlq.Enqueue(msg with { OnDlq = true });
            else
                _main.Enqueue(msg);
        }
    }

    private sealed record ToySqsMessage(string Id, string Body, int ReceiveCount, bool OnDlq);

    private sealed class ToyOutboxStore
    {
        private readonly List<OutboxRow> _rows = new();

        public Task CommitApprovalWithOutboxAsync(Guid requestId, Guid messageId)
        {
            // Pretend domain tables + outbox committed together.
            var body = JsonSerializer.Serialize(new
            {
                schemaVersion = "1",
                messageType = "entitlex.fulfillment.command",
                messageId,
                requestId
            });
            _rows.Add(new OutboxRow(messageId, "Pending", body));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxRow>> ClaimPendingAsync(int batchSize)
        {
            var claimed = _rows.Where(r => r.Status == "Pending").Take(batchSize)
                .Select(r => r with { Status = "InFlight" }).ToList();
            for (var i = 0; i < _rows.Count; i++)
            {
                var match = claimed.FirstOrDefault(c => c.MessageId == _rows[i].MessageId);
                if (match is not null) _rows[i] = match;
            }
            return Task.FromResult<IReadOnlyList<OutboxRow>>(claimed);
        }

        public Task MarkPublishedAsync(Guid messageId)
        {
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].MessageId == messageId)
                    _rows[i] = _rows[i] with { Status = "Published" };
            return Task.CompletedTask;
        }
    }

    private sealed record OutboxRow(Guid MessageId, string Status, string Body);

    private sealed class ToyAuditBucket
    {
        private readonly Dictionary<string, string> _objects = new();
        public string Name { get; }
        public bool Versioned { get; private set; }
        public bool SseKms { get; private set; }
        public bool TlsOnly { get; private set; }
        public int ObjectCount => _objects.Count;
        public ToyAuditBucket(string name) => Name = name;
        public void EnableVersioning() => Versioned = true;
        public void EnableSseKms() => SseKms = true;
        public void DenyInsecureTransport() => TlsOnly = true;
        public void Put(string key, string body) => _objects[key] = body;
    }

    private enum Effect { Allow, Deny }

    private sealed record PolicyStatement(
        Effect Effect,
        string[] Actions,
        string[] Resources,
        bool whenInsecureTransport = false);

    private static class PolicyEvaluator
    {
        public static (bool Allowed, string Reason) Evaluate(
            IEnumerable<PolicyStatement> statements,
            string action,
            string resource,
            bool secureTransport)
        {
            var allowed = false;
            foreach (var s in statements)
            {
                if (!Matches(s.Actions, action) || !Matches(s.Resources, resource))
                    continue;
                if (s.whenInsecureTransport && secureTransport)
                    continue;
                if (s.Effect == Effect.Deny)
                    return (false, "explicit deny");
                if (s.Effect == Effect.Allow)
                    allowed = true;
            }
            return allowed ? (true, "allow") : (false, "implicit deny");
        }

        private static bool Matches(string[] patterns, string value) =>
            patterns.Any(p =>
                p == "*" ||
                p == value ||
                (p.EndsWith('*') && value.StartsWith(p[..^1], StringComparison.Ordinal)));
    }
}
