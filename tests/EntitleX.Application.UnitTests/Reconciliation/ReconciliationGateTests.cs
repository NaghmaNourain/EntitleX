using EntitleX.Application.Abstractions;
using EntitleX.Application.LicenseExtension.Retry;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;

namespace EntitleX.Application.UnitTests.Reconciliation;

public sealed class ReconciliationGateTests
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01Z");

    [Fact]
    public async Task Blocks_WhenSagaReconciliationRequired()
    {
        var (request, saga) = CreateUncertain();
        var gate = new ReconciliationGate(new EmptyLocalOutcomes());

        var reason = await gate.GetRetryBlockReasonAsync(request.Id, saga);

        Assert.NotNull(reason);
        Assert.Contains("ReconciliationRequired", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Blocks_WhenLocalSuccessSagaFailedMismatch()
    {
        var (request, saga) = CreatePartialFailed();
        var failed = request.Items[1];
        var local = new SeededLocalOutcomes(
        [
            new LocalFulfillmentOutcome(
                request.Id.Value,
                failed.Id.Value,
                failed.LogicalOperationId.Value,
                failed.AttemptId.Value,
                failed.EntitlementId.Value,
                "CloudShield",
                Succeeded: true,
                null,
                null,
                DateTimeOffset.UtcNow)
        ]);

        var gate = new ReconciliationGate(local);
        var reason = await gate.GetRetryBlockReasonAsync(request.Id, saga);

        Assert.NotNull(reason);
        Assert.Contains("mismatch", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(failed.LogicalOperationId.Value.ToString("D"), reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allows_WhenPartiallyCompletedWithoutMismatch()
    {
        var (request, saga) = CreatePartialFailed();
        var gate = new ReconciliationGate(new EmptyLocalOutcomes());

        var reason = await gate.GetRetryBlockReasonAsync(request.Id, saga);

        Assert.Null(reason);
    }

    private static (LicenseExtensionRequest Request, LicenseExtensionSaga Saga) CreateUncertain()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-GATE-1",
            "rep@entitlex.local",
            [(EntitlementId.From("ent-1"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.ApplyTimeout(DateTimeOffset.UtcNow.AddHours(7));
        return (request, saga);
    }

    private static (LicenseExtensionRequest Request, LicenseExtensionSaga Saga) CreatePartialFailed()
    {
        var request = LicenseExtensionRequest.CreateFresh(
            "EXT-GATE-2",
            "rep@entitlex.local",
            [
                (EntitlementId.From("ent-1"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30)),
                (EntitlementId.From("ent-2"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(30))
            ]);
        request.Submit();
        request.MarkPendingApproval();
        request.RecordApproval("hash");
        request.BeginFulfillment();
        request.CompleteFulfillment(1, 1);

        var saga = LicenseExtensionSaga.Start(request.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(request.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(request.Items[0].LogicalOperationId);
        saga.RecordItemFailed(request.Items[1].LogicalOperationId, "E", "fail");
        return (request, saga);
    }

    private sealed class EmptyLocalOutcomes : ILocalFulfillmentOutcomeStore
    {
        public Task<LocalFulfillmentOutcome?> FindByLogicalOperationIdAsync(
            LogicalOperationId logicalOperationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalFulfillmentOutcome?>(null);

        public Task<IReadOnlyList<LocalFulfillmentOutcome>> ListByRequestAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalFulfillmentOutcome>>([]);

        public Task<bool> TryRecordAsync(
            LocalFulfillmentOutcome outcome,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class SeededLocalOutcomes(IReadOnlyList<LocalFulfillmentOutcome> rows)
        : ILocalFulfillmentOutcomeStore
    {
        public Task<LocalFulfillmentOutcome?> FindByLogicalOperationIdAsync(
            LogicalOperationId logicalOperationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(rows.FirstOrDefault(r => r.LogicalOperationId == logicalOperationId.Value));

        public Task<IReadOnlyList<LocalFulfillmentOutcome>> ListByRequestAsync(
            RequestId requestId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalFulfillmentOutcome>>(
                rows.Where(r => r.RequestId == requestId.Value).ToList());

        public Task<bool> TryRecordAsync(
            LocalFulfillmentOutcome outcome,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }
}
