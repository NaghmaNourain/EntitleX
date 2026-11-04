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
            [LicenseExtensionRequestStatus.Failed] = [],
            [LicenseExtensionRequestStatus.Cancelled] = []
        };

    private readonly List<LicenseExtensionRequestItem> _items = [];

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
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        ExtensionNumber = extensionNumber;
        RequestedBy = requestedBy;
        ParentId = parentId;
        Status = LicenseExtensionRequestStatus.Draft;
        ApprovalVersion = 0;
        ApprovedScopeHash = null;
        CreatedAtUtc = createdAtUtc;
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

    /// <summary>Optimistic concurrency token (persisted as row_version).</summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyList<LicenseExtensionRequestItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Creates a fresh request. <paramref name="parentId"/> must be null;
    /// use a dedicated retry factory in a later phase for parent lineage.
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
