using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Common;
using EntitleX.Domain.LicenseExtension.Events;

namespace EntitleX.Domain.LicenseExtension;

/// <summary>
/// Aggregate root for a license-extension request. Enforces item-count,
/// parent lineage, expiry baseline, and status-transition invariants.
/// </summary>
public sealed class LicenseExtensionRequest : AggregateRoot
{
    public const int MaxItems = 1000;

    private static readonly IReadOnlyDictionary<LicenseExtensionRequestStatus, LicenseExtensionRequestStatus[]> AllowedTransitions =
        new Dictionary<LicenseExtensionRequestStatus, LicenseExtensionRequestStatus[]>
        {
            [LicenseExtensionRequestStatus.Draft] =
            [
                LicenseExtensionRequestStatus.Submitted,
                LicenseExtensionRequestStatus.Cancelled
            ],
            [LicenseExtensionRequestStatus.Submitted] =
            [
                LicenseExtensionRequestStatus.PendingApproval,
                LicenseExtensionRequestStatus.Cancelled
            ],
            [LicenseExtensionRequestStatus.PendingApproval] =
            [
                LicenseExtensionRequestStatus.Approved,
                LicenseExtensionRequestStatus.Rejected,
                LicenseExtensionRequestStatus.Cancelled
            ],
            [LicenseExtensionRequestStatus.Approved] =
            [
                LicenseExtensionRequestStatus.InFulfillment
            ],
            [LicenseExtensionRequestStatus.Rejected] = [],
            [LicenseExtensionRequestStatus.InFulfillment] =
            [
                LicenseExtensionRequestStatus.PartiallyFulfilled,
                LicenseExtensionRequestStatus.Fulfilled,
                LicenseExtensionRequestStatus.Failed
            ],
            [LicenseExtensionRequestStatus.PartiallyFulfilled] =
            [
                LicenseExtensionRequestStatus.Fulfilled,
                LicenseExtensionRequestStatus.Failed
            ],
            [LicenseExtensionRequestStatus.Fulfilled] = [],
            // Corrective repair when reconciliation promotes Failed → success path.
            [LicenseExtensionRequestStatus.Failed] =
            [
                LicenseExtensionRequestStatus.PartiallyFulfilled,
                LicenseExtensionRequestStatus.Fulfilled
            ],
            [LicenseExtensionRequestStatus.Cancelled] = []
        };

    private readonly List<LicenseExtensionRequestItem> _items = [];
    private readonly List<RetryHistoryEntry> _retryHistory = [];

    private LicenseExtensionRequest()
    {
        // EF Core
        ExtensionNumber = null!;
        RequestedBy = null!;
    }

    private LicenseExtensionRequest(
        RequestId id,
        string extensionNumber,
        string requestedBy,
        RequestId? parentId,
        DateTimeOffset createdAtUtc,
        string? notes = null)
    {
        Id = id;
        ExtensionNumber = extensionNumber;
        RequestedBy = requestedBy;
        ParentId = parentId;
        Status = LicenseExtensionRequestStatus.Draft;
        ApprovalVersion = 0;
        ApprovedScopeHash = null;
        CreatedAtUtc = createdAtUtc;
        Notes = notes;
    }

    public RequestId Id { get; private set; }

    /// <summary>Business-facing unique extension number (DB unique index).</summary>
    public string ExtensionNumber { get; private set; }

    /// <summary>Null for a fresh request; set when this request retries a parent.</summary>
    public RequestId? ParentId { get; private set; }

    public LicenseExtensionRequestStatus Status { get; private set; }

    /// <summary>Increments when an approval decision scope is recorded (Phase 4+).</summary>
    public int ApprovalVersion { get; private set; }

    /// <summary>Hash of the approved item scope; null until approved.</summary>
    public string? ApprovedScopeHash { get; private set; }

    public string RequestedBy { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Optional operator notes (including selective-retry rationale).</summary>
    public string? Notes { get; private set; }

    /// <summary>Optimistic concurrency token (persisted as row_version).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyList<LicenseExtensionRequestItem> Items => _items.AsReadOnly();

    /// <summary>Append-only history of selective retries spawned from this request.</summary>
    public IReadOnlyList<RetryHistoryEntry> RetryHistory => _retryHistory.AsReadOnly();

    /// <summary>
    /// Creates a fresh request. <paramref name="parentId"/> must be null;
    /// use <see cref="CreateSelectiveRetry"/> for parent lineage.
    /// </summary>
    public static LicenseExtensionRequest CreateFresh(
        string extensionNumber,
        string requestedBy,
        IEnumerable<(
            EntitlementId EntitlementId,
            ProviderSystem Provider,
            DateTimeOffset CurrentExpiryUtc,
            DateTimeOffset RequestedExpiryUtc)> items,
        DateTimeOffset? createdAtUtc = null,
        RequestId? parentId = null)
    {
        if (parentId is not null)
        {
            throw new DomainException(
                "Fresh license-extension requests must have a null ParentId.");
        }

        if (string.IsNullOrWhiteSpace(extensionNumber))
        {
            throw new DomainException("ExtensionNumber is required.");
        }

        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            throw new DomainException("RequestedBy is required.");
        }

        var itemList = items?.ToList()
            ?? throw new DomainException("Items collection is required.");

        if (itemList.Count == 0)
        {
            throw new DomainException("At least one item is required.");
        }

        if (itemList.Count > MaxItems)
        {
            throw new DomainException(
                $"A license-extension request cannot contain more than {MaxItems} items.");
        }

        var request = new LicenseExtensionRequest(
            RequestId.New(),
            extensionNumber.Trim(),
            requestedBy.Trim(),
            parentId: null,
            createdAtUtc ?? DateTimeOffset.UtcNow);

        foreach (var item in itemList)
        {
            request._items.Add(LicenseExtensionRequestItem.Create(
                item.EntitlementId,
                item.Provider,
                item.CurrentExpiryUtc,
                item.RequestedExpiryUtc));
        }

        request.RaiseDomainEvent(new LicenseExtensionRequestCreatedDomainEvent(
            request.Id,
            request.ExtensionNumber,
            request.CreatedAtUtc));

        return request;
    }

    /// <summary>
    /// Selective retry factory: copies only the supplied failed parent items,
    /// preserves each <see cref="LogicalOperationId"/>, mints new <see cref="AttemptId"/>s,
    /// and chains via <see cref="ParentId"/>. Successful siblings are never copied
    /// (forward recovery).
    /// </summary>
    public static LicenseExtensionRequest CreateSelectiveRetry(
        LicenseExtensionRequest parent,
        IReadOnlyList<LicenseExtensionRequestItem> failedItemsToRetry,
        string extensionNumber,
        string requestedBy,
        string? notes = null,
        DateTimeOffset? createdAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(failedItemsToRetry);

        if (parent.Status is not (
            LicenseExtensionRequestStatus.PartiallyFulfilled or
            LicenseExtensionRequestStatus.Failed))
        {
            throw new DomainException(
                $"Selective retry requires parent status PartiallyFulfilled or Failed (was '{parent.Status}').");
        }

        if (string.IsNullOrWhiteSpace(extensionNumber))
        {
            throw new DomainException("ExtensionNumber is required.");
        }

        if (string.IsNullOrWhiteSpace(requestedBy))
        {
            throw new DomainException("RequestedBy is required.");
        }

        if (failedItemsToRetry.Count == 0)
        {
            throw new DomainException("Selective retry requires at least one failed item.");
        }

        if (failedItemsToRetry.Count > MaxItems)
        {
            throw new DomainException(
                $"A license-extension request cannot contain more than {MaxItems} items.");
        }

        var parentItemIds = parent.Items.Select(i => i.Id).ToHashSet();
        foreach (var item in failedItemsToRetry)
        {
            if (!parentItemIds.Contains(item.Id))
            {
                throw new DomainException(
                    $"Retry item '{item.Id}' does not belong to parent request '{parent.Id}'.");
            }
        }

        var distinctLogical = failedItemsToRetry
            .Select(i => i.LogicalOperationId)
            .Distinct()
            .Count();
        if (distinctLogical != failedItemsToRetry.Count)
        {
            throw new DomainException("Selective retry items must have distinct LogicalOperationIds.");
        }

        var now = createdAtUtc ?? DateTimeOffset.UtcNow;
        var trimmedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        var request = new LicenseExtensionRequest(
            RequestId.New(),
            extensionNumber.Trim(),
            requestedBy.Trim(),
            parent.Id,
            now,
            trimmedNotes);

        foreach (var source in failedItemsToRetry)
        {
            request._items.Add(LicenseExtensionRequestItem.Create(
                source.EntitlementId,
                source.Provider,
                source.CurrentExpiryUtc,
                source.RequestedExpiryUtc,
                logicalOperationId: source.LogicalOperationId,
                attemptId: AttemptId.New()));
        }

        request.RaiseDomainEvent(new LicenseExtensionRequestCreatedDomainEvent(
            request.Id,
            request.ExtensionNumber,
            request.CreatedAtUtc));

        return request;
    }

    /// <summary>
    /// Records that a selective retry was spawned from this request (append-only).
    /// </summary>
    public void RecordSelectiveRetrySpawned(
        LicenseExtensionRequest retry,
        string createdBy,
        string? notes = null,
        DateTimeOffset? atUtc = null)
    {
        ArgumentNullException.ThrowIfNull(retry);

        if (retry.ParentId is null || !retry.ParentId.Equals(Id))
        {
            throw new DomainException(
                $"Retry '{retry.Id}' is not a child of source '{Id}'.");
        }

        if (_retryHistory.Any(h => h.RetryRequestId.Equals(retry.Id)))
        {
            return;
        }

        _retryHistory.Add(RetryHistoryEntry.Create(
            Id,
            retry.Id,
            retry.ExtensionNumber,
            retry.Items.Count,
            createdBy.Trim(),
            string.IsNullOrWhiteSpace(notes) ? retry.Notes : notes.Trim(),
            atUtc ?? DateTimeOffset.UtcNow));
    }

    public void Submit()
    {
        TransitionTo(LicenseExtensionRequestStatus.Submitted);
    }

    /// <summary>Phase 2 stub: moves a submitted request into the approval queue.</summary>
    public void MarkPendingApproval()
    {
        TransitionTo(LicenseExtensionRequestStatus.PendingApproval);
    }

    /// <summary>Records an approved scope hash and bumps ApprovalVersion.</summary>
    public void RecordApproval(string approvedScopeHash)
    {
        if (string.IsNullOrWhiteSpace(approvedScopeHash))
        {
            throw new DomainException("ApprovedScopeHash is required when recording approval.");
        }

        TransitionTo(LicenseExtensionRequestStatus.Approved);
        ApprovalVersion++;
        ApprovedScopeHash = approvedScopeHash.Trim();
    }

    /// <summary>
    /// Moves an approved request into fulfillment after the fulfillment command
    /// is written to the transactional outbox (same unit of work).
    /// </summary>
    public void BeginFulfillment()
    {
        TransitionTo(LicenseExtensionRequestStatus.InFulfillment);
    }

    /// <summary>
    /// Applies a terminal fulfillment outcome once all item results are known.
    /// Forward recovery only — never rolls back successful sibling extensions.
    /// </summary>
    public void CompleteFulfillment(int succeededCount, int failedCount)
    {
        if (Status is not (
            LicenseExtensionRequestStatus.InFulfillment or
            LicenseExtensionRequestStatus.PartiallyFulfilled or
            LicenseExtensionRequestStatus.Failed))
        {
            throw new DomainException(
                $"Cannot complete fulfillment from status '{Status}'.");
        }

        if (succeededCount < 0 || failedCount < 0)
        {
            throw new DomainException("Fulfillment outcome counts cannot be negative.");
        }

        if (succeededCount + failedCount == 0)
        {
            throw new DomainException("Fulfillment completion requires at least one item result.");
        }

        if (failedCount == 0)
        {
            TransitionTo(LicenseExtensionRequestStatus.Fulfilled);
            return;
        }

        if (succeededCount == 0)
        {
            TransitionTo(LicenseExtensionRequestStatus.Failed);
            return;
        }

        // Mixed outcomes — forward recovery leaves PartiallyFulfilled for selective retry.
        if (Status == LicenseExtensionRequestStatus.InFulfillment)
        {
            TransitionTo(LicenseExtensionRequestStatus.PartiallyFulfilled);
        }
    }

    /// <summary>Rejects a request pending approval (or submitted before queueing).</summary>
    public void Reject()
    {
        if (Status == LicenseExtensionRequestStatus.Submitted)
        {
            TransitionTo(LicenseExtensionRequestStatus.PendingApproval);
        }

        TransitionTo(LicenseExtensionRequestStatus.Rejected);
    }

    /// <summary>Cancels a request that is still cancellable.</summary>
    public void Cancel()
    {
        TransitionTo(LicenseExtensionRequestStatus.Cancelled);
    }

    private void TransitionTo(LicenseExtensionRequestStatus next)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(next))
        {
            throw new DomainException(
                $"Cannot transition license-extension request from '{Status}' to '{next}'.");
        }

        Status = next;
    }
}
