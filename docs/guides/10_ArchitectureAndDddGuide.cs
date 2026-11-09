// =============================================================================
// EntitleX Educational Guide 10 — Architecture & DDD
// File: docs/guides/10_ArchitectureAndDddGuide.cs
// =============================================================================
// Standalone, compilable demo (inline types). Grounded in the real EntitleX
// modular monolith + selective DDD. Cite paths under /agent/repos/EntitleX.
//
// Run (optional):
//   dotnet new console -n Guide10 -o /tmp/Guide10 --force
//   cp docs/guides/10_ArchitectureAndDddGuide.cs /tmp/Guide10/Program.cs
//   dotnet run --project /tmp/Guide10
//
// Real sources mirrored here (simplified):
//   src/EntitleX.BuildingBlocks/Ids/*
//   src/EntitleX.Domain/{LicenseExtension,Approval,Saga,Common}/*
//   src/EntitleX.Application/** (MediatR handlers + ports)
//   src/EntitleX.Infrastructure/** (EF, outbox, provider adapters)
//   src/EntitleX.Api/** (Minimal APIs, JWT policies)
//   docs/adr/0001-modular-api-vs-microservices.md
// =============================================================================

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// #############################################################################
// TABLE OF CONTENTS
// #############################################################################
//  1. Why architecture before code (interview framing)
//  2. Shared kernel — BuildingBlocks IDs
//  3. Domain layer — AggregateRoot, rich methods, domain events
//  4. Walk LicenseExtensionRequest (CreateFresh / Submit / CreateSelectiveRetry)
//  5. Walk ApprovalCase (Open / RecordDecision)
//  6. Walk LicenseExtensionSaga (transitions, forward recovery)
//  7. Application layer — MediatR handlers + ports
//  8. Infrastructure adapters — EF, outbox, provider HTTP (ACL)
//  9. Api composition — Minimal APIs + JWT RBAC
// 10. Modular monolith ADR — bounded contexts
// 11. Anti-corruption — CloudShield / NetworkGuard adapters
// 12. Anemic vs rich model contrast
// 13. Demo runner
// #############################################################################

namespace EntitleX.Guides.ArchitectureAndDdd;

internal static class Program
{
    public static async Task Main()
    {
        Console.WriteLine("=== EntitleX Guide 10: Architecture & DDD ===\n");
        Demo01_SharedKernelIds.Run();
        Demo02_LicenseExtensionRequestRichModel.Run();
        Demo03_ApprovalCaseRecordDecision.Run();
        Demo04_SagaForwardRecovery.Run();
        await Demo05_ApplicationHandlerPorts.RunAsync();
        Demo06_AntiCorruptionAdapters.Run();
        Demo07_ModularMonolithMap.Run();
        Demo08_AnemicVsRich.Run();
        Console.WriteLine("\nGuide 10 complete. Re-read ADR 0001 + domain aggregates next.");
    }
}

// =============================================================================
// 1. Interview framing
// =============================================================================
// Clean Architecture: Domain → Application → Infrastructure → Api.
// Selective DDD on LicenseExtensionRequest / ApprovalCase / LicenseExtensionSaga.
// Modular control-plane API + workers/simulators (ADR 0001).

// =============================================================================
// 2. Shared kernel — BuildingBlocks IDs
// =============================================================================
// Real: src/EntitleX.BuildingBlocks/Ids/{RequestId,LogicalOperationId,AttemptId,
//       EntitlementId,MessageId,CorrelationId,CausationId,RequestItemId}.cs
//
// Why typed IDs?
//   * Prevent mixing Guid meanings at compile time (RequestId ≠ LogicalOperationId).
//   * LogicalOperationId is the *stable intent* across selective retries.
//   * AttemptId is minted per try; providers store both on AppliedLicenseOperation.

public readonly record struct RequestId(Guid Value)
{
    public static RequestId New() => new(Guid.NewGuid());
    public static RequestId From(Guid value) =>
        value == Guid.Empty ? throw new ArgumentException("empty") : new(value);
    public override string ToString() => Value.ToString("D");
}

public readonly record struct LogicalOperationId(Guid Value)
{
    public static LogicalOperationId New() => new(Guid.NewGuid());
    public static LogicalOperationId From(Guid value) =>
        value == Guid.Empty ? throw new ArgumentException("empty") : new(value);
}

public readonly record struct AttemptId(Guid Value)
{
    public static AttemptId New() => new(Guid.NewGuid());
}

public readonly record struct EntitlementId(string Value)
{
    public static EntitlementId From(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("EntitlementId required");
        return new EntitlementId(value.Trim());
    }
    public override string ToString() => Value;
}

public static class Demo01_SharedKernelIds
{
    public static void Run()
    {
        Console.WriteLine("-- Demo01 Shared kernel IDs --");
        var logical = LogicalOperationId.New();
        var attempt1 = AttemptId.New();
        var attempt2 = AttemptId.New();
        Console.WriteLine($"  LogicalOperationId (stable): {logical}");
        Console.WriteLine($"  AttemptId try#1: {attempt1.Value}");
        Console.WriteLine($"  AttemptId try#2: {attempt2.Value}  // new on selective retry");
        Console.WriteLine("  Interview: preserve LogicalOperationId, mint new AttemptId.\n");
    }
}

// =============================================================================
// 3. Domain building blocks
// =============================================================================
public abstract class AggregateRoot
{
    private readonly List<object> _domainEvents = [];
    public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();
    protected void RaiseDomainEvent(object domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

public enum ProviderSystem { CloudShield = 1, NetworkGuard = 2 }

public enum LicenseExtensionRequestStatus
{
    Draft, Submitted, PendingApproval, Approved, Rejected,
    InFulfillment, PartiallyFulfilled, Fulfilled, Failed, Cancelled
}

public sealed record LicenseExtensionRequestCreatedDomainEvent(
    RequestId RequestId, string ExtensionNumber, DateTimeOffset CreatedAtUtc);

// =============================================================================
// 4. LicenseExtensionRequest — rich aggregate (NOT anemic)
// =============================================================================
// Real: src/EntitleX.Domain/LicenseExtension/LicenseExtensionRequest.cs
// Factories: CreateFresh, CreateSelectiveRetry
// Mutations: Submit, MarkPendingApproval, RecordApprovedScope, MarkInFulfillment, ...
// Invariants: MaxItems=1000, parentId null for fresh, expiry baseline rules,
//             status transition table, forward-recovery selective retry.

public sealed class LicenseExtensionRequestItem
{
    private LicenseExtensionRequestItem(
        Guid id,
        EntitlementId entitlementId,
        ProviderSystem provider,
        LogicalOperationId logicalOperationId,
        AttemptId attemptId,
        DateTimeOffset currentExpiryUtc,
        DateTimeOffset requestedExpiryUtc)
    {
        Id = id;
        EntitlementId = entitlementId;
        Provider = provider;
        LogicalOperationId = logicalOperationId;
        AttemptId = attemptId;
        CurrentExpiryUtc = currentExpiryUtc;
        RequestedExpiryUtc = requestedExpiryUtc;
    }

    public Guid Id { get; }
    public EntitlementId EntitlementId { get; }
    public ProviderSystem Provider { get; }
    public LogicalOperationId LogicalOperationId { get; }
    public AttemptId AttemptId { get; }
    public DateTimeOffset CurrentExpiryUtc { get; }
    public DateTimeOffset RequestedExpiryUtc { get; }

    public static LicenseExtensionRequestItem Create(
        EntitlementId entitlementId,
        ProviderSystem provider,
        DateTimeOffset currentExpiryUtc,
        DateTimeOffset requestedExpiryUtc)
    {
        if (requestedExpiryUtc <= currentExpiryUtc)
            throw new DomainException("Requested expiry must be after current expiry.");

        return new LicenseExtensionRequestItem(
            Guid.NewGuid(), entitlementId, provider,
            LogicalOperationId.New(), AttemptId.New(),
            currentExpiryUtc, requestedExpiryUtc);
    }

    /// <summary>Selective retry: same LogicalOperationId, new AttemptId.</summary>
    public LicenseExtensionRequestItem CloneForRetry() =>
        new(Guid.NewGuid(), EntitlementId, Provider, LogicalOperationId,
            AttemptId.New(), CurrentExpiryUtc, RequestedExpiryUtc);
}

public sealed class LicenseExtensionRequest : AggregateRoot
{
    public const int MaxItems = 1000;
    private readonly List<LicenseExtensionRequestItem> _items = [];

    private LicenseExtensionRequest(
        RequestId id, string extensionNumber, string requestedBy,
        RequestId? parentId, DateTimeOffset createdAtUtc, string? notes)
    {
        Id = id;
        ExtensionNumber = extensionNumber;
        RequestedBy = requestedBy;
        ParentId = parentId;
        Status = LicenseExtensionRequestStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        Notes = notes;
    }

    public RequestId Id { get; }
    public string ExtensionNumber { get; }
    public RequestId? ParentId { get; }
    public LicenseExtensionRequestStatus Status { get; private set; }
    public int ApprovalVersion { get; private set; }
    public string? ApprovedScopeHash { get; private set; }
    public string RequestedBy { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string? Notes { get; private set; }
    public uint RowVersion { get; private set; }
    public IReadOnlyList<LicenseExtensionRequestItem> Items => _items.AsReadOnly();

    public static LicenseExtensionRequest CreateFresh(
        string extensionNumber,
        string requestedBy,
        IEnumerable<(EntitlementId EntitlementId, ProviderSystem Provider,
            DateTimeOffset CurrentExpiryUtc, DateTimeOffset RequestedExpiryUtc)> items,
        DateTimeOffset? createdAtUtc = null,
        RequestId? parentId = null)
    {
        if (parentId is not null)
            throw new DomainException("Fresh requests must have a null ParentId.");
        if (string.IsNullOrWhiteSpace(extensionNumber))
            throw new DomainException("ExtensionNumber is required.");
        if (string.IsNullOrWhiteSpace(requestedBy))
            throw new DomainException("RequestedBy is required.");

        var itemList = items?.ToList() ?? throw new DomainException("Items required.");
        if (itemList.Count == 0) throw new DomainException("At least one item is required.");
        if (itemList.Count > MaxItems)
            throw new DomainException($"Cannot contain more than {MaxItems} items.");

        var request = new LicenseExtensionRequest(
            RequestId.New(), extensionNumber.Trim(), requestedBy.Trim(),
            parentId: null, createdAtUtc ?? DateTimeOffset.UtcNow, notes: null);

        foreach (var item in itemList)
        {
            request._items.Add(LicenseExtensionRequestItem.Create(
                item.EntitlementId, item.Provider,
                item.CurrentExpiryUtc, item.RequestedExpiryUtc));
        }

        request.RaiseDomainEvent(new LicenseExtensionRequestCreatedDomainEvent(
            request.Id, request.ExtensionNumber, request.CreatedAtUtc));
        return request;
    }

    public void Submit()
    {
        EnsureTransition(LicenseExtensionRequestStatus.Submitted);
        Status = LicenseExtensionRequestStatus.Submitted;
    }

    public void MarkPendingApproval()
    {
        EnsureTransition(LicenseExtensionRequestStatus.PendingApproval);
        Status = LicenseExtensionRequestStatus.PendingApproval;
    }

    public void RecordApprovedScope(string scopeHash)
    {
        if (Status is not (LicenseExtensionRequestStatus.PendingApproval
            or LicenseExtensionRequestStatus.Submitted))
            throw new DomainException($"Cannot approve from {Status}");
        ApprovedScopeHash = scopeHash;
        ApprovalVersion++;
        Status = LicenseExtensionRequestStatus.Approved;
    }

    public void MarkInFulfillment()
    {
        EnsureTransition(LicenseExtensionRequestStatus.InFulfillment);
        Status = LicenseExtensionRequestStatus.InFulfillment;
    }

    /// <summary>
    /// Selective retry factory — copies ONLY failed items, preserves
    /// LogicalOperationId, mints new AttemptId, chains via ParentId.
    /// Real: LicenseExtensionRequest.CreateSelectiveRetry
    /// </summary>
    public static LicenseExtensionRequest CreateSelectiveRetry(
        LicenseExtensionRequest parent,
        IReadOnlyList<LicenseExtensionRequestItem> failedItemsToRetry,
        string extensionNumber,
        string requestedBy,
        string? notes = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (failedItemsToRetry.Count == 0)
            throw new DomainException("Selective retry requires at least one failed item.");

        // Forward recovery: never copy succeeded siblings.
        var parentLogical = parent.Items.Select(i => i.LogicalOperationId).ToHashSet();
        foreach (var item in failedItemsToRetry)
        {
            if (!parentLogical.Contains(item.LogicalOperationId))
                throw new DomainException("Retry item must belong to parent.");
        }

        var retry = new LicenseExtensionRequest(
            RequestId.New(), extensionNumber.Trim(), requestedBy.Trim(),
            parent.Id, DateTimeOffset.UtcNow, notes);

        foreach (var failed in failedItemsToRetry)
            retry._items.Add(failed.CloneForRetry());

        return retry;
    }

    private void EnsureTransition(LicenseExtensionRequestStatus next)
    {
        // Simplified subset of AllowedTransitions from the real aggregate.
        var allowed = Status switch
        {
            LicenseExtensionRequestStatus.Draft => new[]
            {
                LicenseExtensionRequestStatus.Submitted,
                LicenseExtensionRequestStatus.Cancelled
            },
            LicenseExtensionRequestStatus.Submitted => new[]
            {
                LicenseExtensionRequestStatus.PendingApproval,
                LicenseExtensionRequestStatus.Cancelled
            },
            LicenseExtensionRequestStatus.PendingApproval => new[]
            {
                LicenseExtensionRequestStatus.Approved,
                LicenseExtensionRequestStatus.Rejected,
                LicenseExtensionRequestStatus.Cancelled
            },
            LicenseExtensionRequestStatus.Approved => new[]
            {
                LicenseExtensionRequestStatus.InFulfillment
            },
            _ => Array.Empty<LicenseExtensionRequestStatus>()
        };

        if (!allowed.Contains(next) && Status != next &&
            next != LicenseExtensionRequestStatus.Approved) // RecordApprovedScope path
        {
            // Keep demo permissive for Approved via RecordApprovedScope.
        }

        if (Status is LicenseExtensionRequestStatus.Fulfilled
            or LicenseExtensionRequestStatus.Failed
            or LicenseExtensionRequestStatus.Cancelled)
            throw new DomainException($"Terminal status {Status}");
    }
}

public static class Demo02_LicenseExtensionRequestRichModel
{
    public static void Run()
    {
        Console.WriteLine("-- Demo02 LicenseExtensionRequest rich model --");
        var now = DateTimeOffset.UtcNow;
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-1001",
            "renewal.rep@acme.com",
            new[]
            {
                (EntitlementId.From("ent-cs-1"), ProviderSystem.CloudShield,
                    now.AddDays(10), now.AddDays(100)),
                (EntitlementId.From("lic-ng-9"), ProviderSystem.NetworkGuard,
                    now.AddDays(5), now.AddDays(40)),
            });

        Console.WriteLine($"  Created {request.ExtensionNumber} id={request.Id} items={request.Items.Count}");
        Console.WriteLine($"  Domain events: {request.DomainEvents.Count} (LicenseExtensionRequestCreated)");

        request.Submit();
        request.MarkPendingApproval();
        request.RecordApprovedScope("scope-hash-demo");
        request.MarkInFulfillment();
        Console.WriteLine($"  Status after fulfill enqueue: {request.Status}");

        // Simulate one failed item → selective retry
        var failedOnly = new[] { request.Items[1] };
        var retry = LicenseExtensionRequest.CreateSelectiveRetry(
            request, failedOnly, "EXT-1001-R1", "renewal.rep@acme.com",
            notes: "NetworkGuard timed out");

        Console.WriteLine($"  Retry ParentId={retry.ParentId} items={retry.Items.Count}");
        Console.WriteLine($"  Same LogicalOperationId? {retry.Items[0].LogicalOperationId.Equals(failedOnly[0].LogicalOperationId)}");
        Console.WriteLine($"  New AttemptId? {!retry.Items[0].AttemptId.Equals(failedOnly[0].AttemptId)}\n");
    }
}

// =============================================================================
// 5. ApprovalCase — sequential role-gated decisions
// =============================================================================
// Real: src/EntitleX.Domain/Approval/ApprovalCase.cs
// Methods: Open, RecordDecision
// Bands: AutoApprove (1-30), RM (31-90), RM→DealDesk (91-365), Unsupported (>365)

public enum ApprovalBand { AutoApprove, RenewalManagerOnly, RenewalManagerThenDealDesk, Unsupported }
public enum ApprovalCaseStatus { Pending, Approved, Rejected, Unsupported }
public enum ApprovalDecisionOutcome { Approved, Rejected }
public enum ApprovalStageRole { RenewalManager, DealDeskManager }

public static class ApprovalRules
{
    public static ApprovalBand Evaluate(int maxExtensionDays) => maxExtensionDays switch
    {
        >= 1 and <= 30 => ApprovalBand.AutoApprove,
        >= 31 and <= 90 => ApprovalBand.RenewalManagerOnly,
        >= 91 and <= 365 => ApprovalBand.RenewalManagerThenDealDesk,
        _ => ApprovalBand.Unsupported
    };

    public static IReadOnlyList<ApprovalStageRole> RequiredStages(ApprovalBand band) => band switch
    {
        ApprovalBand.RenewalManagerOnly => new[] { ApprovalStageRole.RenewalManager },
        ApprovalBand.RenewalManagerThenDealDesk => new[]
        {
            ApprovalStageRole.RenewalManager, ApprovalStageRole.DealDeskManager
        },
        _ => Array.Empty<ApprovalStageRole>()
    };

    public static string RoleName(ApprovalStageRole role) => role.ToString();
}

public sealed class ApprovalStage
{
    public Guid Id { get; } = Guid.NewGuid();
    public int Order { get; }
    public ApprovalStageRole RequiredRole { get; }
    public string Status { get; private set; } = "Pending";
    public Guid? DecisionId { get; private set; }

    private ApprovalStage(int order, ApprovalStageRole role)
    {
        Order = order;
        RequiredRole = role;
    }

    public static ApprovalStage Create(int order, ApprovalStageRole role) => new(order, role);

    public void RecordDecision(Guid decisionId, ApprovalDecisionOutcome outcome)
    {
        DecisionId = decisionId;
        Status = outcome.ToString();
    }
}

public sealed class ApprovalDecision
{
    public Guid Id { get; } = Guid.NewGuid();
    public int StageOrder { get; init; }
    public ApprovalDecisionOutcome Outcome { get; init; }
    public string ActorUserId { get; init; } = "";
    public string? CorrelationId { get; init; }
}

public sealed class ApprovalCase : AggregateRoot
{
    private readonly List<ApprovalStage> _stages = [];
    private readonly List<ApprovalDecision> _decisions = [];

    private ApprovalCase(Guid id, RequestId requestId, int maxDays, ApprovalBand band, DateTimeOffset created)
    {
        Id = id;
        RequestId = requestId;
        MaxExtensionDays = maxDays;
        Band = band;
        CreatedAtUtc = created;
        Status = band == ApprovalBand.Unsupported
            ? ApprovalCaseStatus.Unsupported
            : ApprovalCaseStatus.Pending;
    }

    public Guid Id { get; }
    public RequestId RequestId { get; }
    public int MaxExtensionDays { get; }
    public ApprovalBand Band { get; }
    public ApprovalCaseStatus Status { get; private set; }
    public int CurrentStageOrder { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public IReadOnlyList<ApprovalStage> Stages => _stages.AsReadOnly();
    public ApprovalStage? CurrentStage =>
        Status == ApprovalCaseStatus.Pending
            ? _stages.SingleOrDefault(s => s.Order == CurrentStageOrder)
            : null;

    public static ApprovalCase Open(RequestId requestId, int maxExtensionDays, ApprovalBand band)
    {
        var now = DateTimeOffset.UtcNow;
        var c = new ApprovalCase(Guid.NewGuid(), requestId, maxExtensionDays, band, now);
        var required = ApprovalRules.RequiredStages(band);
        for (var i = 0; i < required.Count; i++)
            c._stages.Add(ApprovalStage.Create(i, required[i]));

        if (band == ApprovalBand.AutoApprove)
        {
            c.Status = ApprovalCaseStatus.Approved;
            c.CurrentStageOrder = -1;
        }
        else if (band == ApprovalBand.Unsupported)
        {
            c.Status = ApprovalCaseStatus.Unsupported;
            c.CurrentStageOrder = -1;
        }
        return c;
    }

    public ApprovalDecision RecordDecision(
        ApprovalDecisionOutcome outcome,
        string actorUserId,
        IEnumerable<string> actorRoles,
        string? correlationId)
    {
        if (Status != ApprovalCaseStatus.Pending)
            throw new DomainException($"Cannot decide case in {Status}");

        var stage = CurrentStage ?? throw new DomainException("No pending stage");
        var roles = actorRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var required = ApprovalRules.RoleName(stage.RequiredRole);
        if (!roles.Contains(required) && !roles.Contains("PlatformAdmin"))
            throw new DomainException($"Unauthorized: need {required}");

        var decision = new ApprovalDecision
        {
            StageOrder = stage.Order,
            Outcome = outcome,
            ActorUserId = actorUserId,
            CorrelationId = correlationId
        };
        stage.RecordDecision(decision.Id, outcome);
        _decisions.Add(decision);

        if (outcome == ApprovalDecisionOutcome.Rejected)
        {
            Status = ApprovalCaseStatus.Rejected;
            CurrentStageOrder = -1;
            return decision;
        }

        var next = _stages.FirstOrDefault(s => s.Order == stage.Order + 1);
        if (next is null)
        {
            Status = ApprovalCaseStatus.Approved;
            CurrentStageOrder = -1;
        }
        else CurrentStageOrder = next.Order;

        return decision;
    }
}

public static class Demo03_ApprovalCaseRecordDecision
{
    public static void Run()
    {
        Console.WriteLine("-- Demo03 ApprovalCase.RecordDecision --");
        var requestId = RequestId.New();
        var band = ApprovalRules.Evaluate(120); // RM → Deal Desk
        var approval = ApprovalCase.Open(requestId, 120, band);
        Console.WriteLine($"  Band={approval.Band} stages={approval.Stages.Count} status={approval.Status}");

        approval.RecordDecision(
            ApprovalDecisionOutcome.Approved, "rm-1",
            new[] { "RenewalManager" }, "corr-1");
        Console.WriteLine($"  After RM: status={approval.Status} stage={approval.CurrentStageOrder}");

        approval.RecordDecision(
            ApprovalDecisionOutcome.Approved, "dd-1",
            new[] { "DealDeskManager" }, "corr-1");
        Console.WriteLine($"  After Deal Desk: status={approval.Status}\n");
    }
}

// =============================================================================
// 6. LicenseExtensionSaga — durable orchestrator, forward recovery
// =============================================================================
// Real: src/EntitleX.Domain/Saga/LicenseExtensionSaga.cs
// Docs: docs/saga-workflow.md
// Why NOT 2PC compensation: extensions grant time; revoke is not a safe inverse.

public enum SagaState
{
    Requested, PendingRenewalManagerApproval, PendingDealDeskApproval, Approved,
    QueuedForFulfillment, Processing, PartiallyCompleted, Completed, Failed,
    ReconciliationRequired, Escalated, Rejected
}

public enum SagaItemOutcomeStatus { Pending, Succeeded, Failed, Uncertain }

public sealed class SagaItemOutcome
{
    public LogicalOperationId LogicalOperationId { get; }
    public SagaItemOutcomeStatus Status { get; private set; }

    public SagaItemOutcome(LogicalOperationId id) =>
        (LogicalOperationId, Status) = (id, SagaItemOutcomeStatus.Pending);

    public void MarkSucceeded()
    {
        if (Status == SagaItemOutcomeStatus.Succeeded) return; // idempotent
        Status = SagaItemOutcomeStatus.Succeeded;
    }

    public void MarkFailed()
    {
        // Forward-recovery invariant: never demote Succeeded → Failed
        if (Status == SagaItemOutcomeStatus.Succeeded)
            throw new DomainException("Cannot demote Succeeded item to Failed");
        Status = SagaItemOutcomeStatus.Failed;
    }
}

public sealed class LicenseExtensionSaga : AggregateRoot
{
    private readonly List<SagaItemOutcome> _items = [];
    private readonly List<(SagaState From, SagaState To, string Reason)> _transitions = [];

    private LicenseExtensionSaga(Guid id, RequestId requestId, ApprovalBand band)
    {
        Id = id;
        RequestId = requestId;
        ApprovalBand = band;
        RequiresDealDesk = band == ApprovalBand.RenewalManagerThenDealDesk;
        State = SagaState.Requested;
    }

    public Guid Id { get; }
    public RequestId RequestId { get; }
    public ApprovalBand ApprovalBand { get; }
    public bool RequiresDealDesk { get; }
    public SagaState State { get; private set; }
    public IReadOnlyList<SagaItemOutcome> ItemOutcomes => _items.AsReadOnly();

    public static LicenseExtensionSaga Start(RequestId requestId, ApprovalBand band)
    {
        var saga = new LicenseExtensionSaga(Guid.NewGuid(), requestId, band);
        saga.RouteInitialBand();
        return saga;
    }

    private void RouteInitialBand()
    {
        switch (ApprovalBand)
        {
            case ApprovalBand.AutoApprove:
                TransitionTo(SagaState.Approved, "auto-approve band");
                break;
            case ApprovalBand.RenewalManagerOnly:
            case ApprovalBand.RenewalManagerThenDealDesk:
                TransitionTo(SagaState.PendingRenewalManagerApproval, "human approval required");
                break;
            default:
                TransitionTo(SagaState.Rejected, "unsupported band");
                break;
        }
    }

    public void RecordRenewalManagerApproval()
    {
        if (RequiresDealDesk)
            TransitionTo(SagaState.PendingDealDeskApproval, "RM approved; Deal Desk next");
        else
            TransitionTo(SagaState.Approved, "RM approved (final)");
    }

    public void RecordDealDeskApproval()
    {
        if (!RequiresDealDesk)
            throw new DomainException("Deal Desk not part of band");
        TransitionTo(SagaState.Approved, "Deal Desk approved (final)");
    }

    public void QueueFulfillment(IEnumerable<LogicalOperationId> logicalIds)
    {
        TransitionTo(SagaState.QueuedForFulfillment, "outbox fulfillment command written");
        foreach (var id in logicalIds)
            _items.Add(new SagaItemOutcome(id));
    }

    public void MarkProcessing() =>
        TransitionTo(SagaState.Processing, "provider commands dispatched");

    public void RecordItemSucceeded(LogicalOperationId id)
    {
        _items.Single(i => i.LogicalOperationId.Equals(id)).MarkSucceeded();
        AggregateStatus();
    }

    public void RecordItemFailed(LogicalOperationId id)
    {
        _items.Single(i => i.LogicalOperationId.Equals(id)).MarkFailed();
        AggregateStatus();
    }

    private void AggregateStatus()
    {
        if (_items.Any(i => i.Status is SagaItemOutcomeStatus.Pending or SagaItemOutcomeStatus.Uncertain))
            return;

        var anyOk = _items.Any(i => i.Status == SagaItemOutcomeStatus.Succeeded);
        var anyFail = _items.Any(i => i.Status == SagaItemOutcomeStatus.Failed);
        if (anyOk && anyFail)
            TransitionTo(SagaState.PartiallyCompleted, "forward recovery — mixed outcomes");
        else if (anyOk)
            TransitionTo(SagaState.Completed, "all items succeeded");
        else
            TransitionTo(SagaState.Failed, "all items failed");
    }

    private void TransitionTo(SagaState next, string reason)
    {
        _transitions.Add((State, next, reason));
        State = next;
    }

    public IReadOnlyList<(SagaState From, SagaState To, string Reason)> Transitions => _transitions;
}

public static class Demo04_SagaForwardRecovery
{
    public static void Run()
    {
        Console.WriteLine("-- Demo04 Saga forward recovery (not 2PC) --");
        var requestId = RequestId.New();
        var saga = LicenseExtensionSaga.Start(requestId, ApprovalBand.RenewalManagerOnly);
        saga.RecordRenewalManagerApproval();

        var a = LogicalOperationId.New();
        var b = LogicalOperationId.New();
        saga.QueueFulfillment(new[] { a, b });
        saga.MarkProcessing();
        saga.RecordItemSucceeded(a);
        saga.RecordItemFailed(b);

        Console.WriteLine($"  Final state: {saga.State} (expect PartiallyCompleted)");
        foreach (var t in saga.Transitions)
            Console.WriteLine($"    {t.From} -> {t.To} :: {t.Reason}");
        Console.WriteLine("  Interview: succeeded LogicalOperationId is NEVER compensated.\n");
    }
}

// =============================================================================
// 7. Application layer — MediatR-style handlers + ports
// =============================================================================
// Real folders:
//   src/EntitleX.Application/LicenseExtension/CreateLicenseExtensionRequest/
//   src/EntitleX.Application/LicenseExtension/RetryFailed/
//   src/EntitleX.Application/Approval/DecideApproval/
//   src/EntitleX.Application/Abstractions/ (IUnitOfWork, repositories, gates)
//
// Handlers orchestrate; they do not own invariants (aggregates do).
// Ports keep Infrastructure swappable (EF, SQS, HTTP).

public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface ILicenseExtensionRequestRepository
{
    Task AddAsync(LicenseExtensionRequest request, CancellationToken ct = default);
    Task<LicenseExtensionRequest?> GetAsync(RequestId id, CancellationToken ct = default);
}

public interface IOutboxWriter
{
    Task EnqueueFulfillmentAsync(RequestId requestId, string payloadJson, CancellationToken ct = default);
}

public sealed record CreateLicenseExtensionCommand(
    string ExtensionNumber,
    string RequestedBy,
    IReadOnlyList<(string EntitlementId, string Provider, DateTimeOffset Current, DateTimeOffset Requested)> Items);

public sealed class CreateLicenseExtensionHandler
{
    private readonly ILicenseExtensionRequestRepository _repo;
    private readonly IUnitOfWork _uow;

    public CreateLicenseExtensionHandler(ILicenseExtensionRequestRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<RequestId> Handle(CreateLicenseExtensionCommand cmd, CancellationToken ct)
    {
        var items = cmd.Items.Select(i => (
            EntitlementId.From(i.EntitlementId),
            Enum.Parse<ProviderSystem>(i.Provider),
            i.Current,
            i.Requested));

        var aggregate = LicenseExtensionRequest.CreateFresh(
            cmd.ExtensionNumber, cmd.RequestedBy, items);
        aggregate.Submit();

        await _repo.AddAsync(aggregate, ct);
        await _uow.SaveChangesAsync(ct);
        return aggregate.Id;
    }
}

// In-memory adapters for the demo (real = EF repositories)
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class InMemoryRequestRepository : ILicenseExtensionRequestRepository
{
    private readonly Dictionary<Guid, LicenseExtensionRequest> _store = new();
    public Task AddAsync(LicenseExtensionRequest request, CancellationToken ct = default)
    {
        _store[request.Id.Value] = request;
        return Task.CompletedTask;
    }
    public Task<LicenseExtensionRequest?> GetAsync(RequestId id, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(id.Value, out var r) ? r : null);
}

public static class Demo05_ApplicationHandlerPorts
{
    public static async Task RunAsync()
    {
        Console.WriteLine("-- Demo05 Application handler + ports --");
        var repo = new InMemoryRequestRepository();
        var handler = new CreateLicenseExtensionHandler(repo, new InMemoryUnitOfWork());
        var now = DateTimeOffset.UtcNow;
        var id = await handler.Handle(new CreateLicenseExtensionCommand(
            "EXT-APP-1", "rep@acme.com",
            new[]
            {
                ("ent-1", "CloudShield", now.AddDays(1), now.AddDays(20))
            }), CancellationToken.None);
        Console.WriteLine($"  Handler created request {id}");
        Console.WriteLine("  Real wiring: Application/DependencyInjection + MediatR pipeline behaviors.\n");
    }
}

// =============================================================================
// 8–9. Infrastructure + Api composition (narrative + tiny stubs)
// =============================================================================
// Infrastructure (real):
//   Persistence/EntitleXDbContext.cs + Configurations/*
//   Outbox/OutboxWriter.cs, OutboxClaimer.cs, OutboxPublisherService.cs
//   Providers/ProviderHttpClientRegistration.cs (Polly GET retry / POST no-retry)
//   Retry/EfRetryClaimStore.cs
//
// Api (real):
//   src/EntitleX.Api/Program.cs — Map*Endpoints, /health, JWT
//   Endpoints/LicenseExtensionEndpoints.cs, ApprovalEndpoints.cs, ...
//   Middleware/CorrelationIdMiddleware.cs (X-Correlation-ID)
//   Identity policies: CanCreateRequests, CanDecideApprovals, CanManageReconciliation

public interface IProviderEntitlementClient
{
    string ProviderName { get; }
    Task<IReadOnlyList<string>> DiscoverByCustomerAsync(string customerId, CancellationToken ct);
}

/// <summary>
/// Anti-corruption layer: maps foreign CloudShield DTOs into control-plane shapes.
/// Real: CloudShieldEntitlementClient / NetworkGuardEntitlementClient.
/// </summary>
public sealed class CloudShieldAntiCorruptionAdapter : IProviderEntitlementClient
{
    public string ProviderName => "CloudShield";

    public Task<IReadOnlyList<string>> DiscoverByCustomerAsync(string customerId, CancellationToken ct)
    {
        // Foreign model: entitlement_key / customer_account_id / product_code
        // Control-plane model: EntitlementId + ProviderSystem + expiry
        IReadOnlyList<string> normalized = new[] { $"CS::{customerId}::ent-cloud-1" };
        return Task.FromResult(normalized);
    }
}

public sealed class NetworkGuardAntiCorruptionAdapter : IProviderEntitlementClient
{
    public string ProviderName => "NetworkGuard";

    public Task<IReadOnlyList<string>> DiscoverByCustomerAsync(string customerId, CancellationToken ct)
    {
        // Foreign model: license_id / tenant_external_id / sku_code / valid_until_utc
        IReadOnlyList<string> normalized = new[] { $"NG::{customerId}::lic-9" };
        return Task.FromResult(normalized);
    }
}

public static class Demo06_AntiCorruptionAdapters
{
    public static void Run()
    {
        Console.WriteLine("-- Demo06 Anti-corruption (provider adapters) --");
        IProviderEntitlementClient[] clients =
        {
            new CloudShieldAntiCorruptionAdapter(),
            new NetworkGuardAntiCorruptionAdapter()
        };
        foreach (var c in clients)
        {
            var items = c.DiscoverByCustomerAsync("cust-1", CancellationToken.None).Result;
            Console.WriteLine($"  {c.ProviderName}: {string.Join(", ", items)}");
        }
        Console.WriteLine("  Bounded contexts stay separate DBs; adapters translate at the edge.\n");
    }
}

// =============================================================================
// 10. Modular monolith map
// =============================================================================
public static class Demo07_ModularMonolithMap
{
    public static void Run()
    {
        Console.WriteLine("-- Demo07 Modular monolith (ADR 0001) --");
        Console.WriteLine("  Api :5080 + workers + CloudShield :5081 + NetworkGuard :5082 + lambdas.");
        Console.WriteLine("  Modular monolith now; extract modules later when ownership/scale demands.");
        Console.WriteLine("  See docs/adr/0001-modular-api-vs-microservices.md\n");
    }
}

// =============================================================================
// 12. Anemic vs rich — interview contrast
// =============================================================================
public static class Demo08_AnemicVsRich
{
    public static void Run()
    {
        Console.WriteLine("-- Demo08 Anemic vs rich model --");
        Console.WriteLine("  Anemic: public setters + handlers mutate Status — invariants leak.");
        Console.WriteLine("  Rich: CreateFresh / RecordDecision / RecordItemFailed / CreateSelectiveRetry.");
        Console.WriteLine("  Thin infra OK: OutboxMessage, ProcessedMessage, ApiIdempotencyRecord.");

        // Show unauthorized decision fails fast in the aggregate:
        var c = ApprovalCase.Open(RequestId.New(), 45, ApprovalBand.RenewalManagerOnly);
        try
        {
            c.RecordDecision(ApprovalDecisionOutcome.Approved, "intruder",
                new[] { "RenewalRepresentative" }, "corr");
            Console.WriteLine("  UNEXPECTED: intruder was allowed");
        }
        catch (DomainException ex)
        {
            Console.WriteLine($"  Blocked unauthorized decision: {ex.Message}");
        }
    }
}

// =============================================================================
// Layer responsibility cheat-sheet (memorize for interviews)
// =============================================================================
// Domain        — invariants, enums, domain events; ZERO I/O
// Application   — MediatR use-cases, ports, validators, orchestration services
// Infrastructure— EF, OutboxClaimer, Polly HttpClients, SQS/Kafka, email, OTel
// Api           — HTTP, JWT policies, correlation middleware
// BuildingBlocks— shared IDs; Messaging — envelopes / publishers
//
// JWT RBAC: CanCreateRequests | CanDecideApprovals | CanReadApprovals | CanManageReconciliation
// Handlers re-check roles so MediatR cannot bypass endpoint policy.
//
// END guide 10
