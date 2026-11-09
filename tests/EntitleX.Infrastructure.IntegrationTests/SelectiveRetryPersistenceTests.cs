using EntitleX.Application.Abstractions;
using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.Approval;
using EntitleX.Domain.LicenseExtension;
using EntitleX.Domain.Saga;
using EntitleX.Infrastructure.Outbox;
using EntitleX.Infrastructure.Persistence;
using EntitleX.Infrastructure.Retry;
using EntitleX.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace EntitleX.Infrastructure.IntegrationTests;

/// <summary>
/// Selective retry must persist a second saga with the same LogicalOperationId
/// and settle provider results keyed by AttemptId.
/// </summary>
public sealed class SelectiveRetryPersistenceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Baseline = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

    private PostgreSqlContainer? _container;

    public async Task InitializeAsync()
    {
        if (!DockerAvailability.IsAvailable)
        {
            return;
        }

        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .WithDatabase("entitlex_platform")
            .WithUsername("entitlex")
            .WithPassword("entitlex")
            .Build();

        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    [DockerFact]
    public async Task SelectiveRetry_PersistsSecondSaga_SameLogicalOp_AndSettlesNewAttempt()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var (parent, parentSaga, failedItem) = await SeedFailedParentAsync(db);

        var selected = FailedItemSelector.SelectEligibleFailedItems(parent, parentSaga);
        var retry = LicenseExtensionRequest.CreateSelectiveRetry(
            parent,
            selected,
            "EXT-RETRY-1",
            "rep@entitlex.local",
            notes: "retry");
        retry.Submit();
        retry.MarkPendingApproval();
        retry.RecordApproval(ApprovedScopeHasher.Compute(retry));

        var retrySaga = LicenseExtensionSaga.Start(retry.Id, ApprovalBand.AutoApprove);
        retrySaga.QueueFulfillment(
            retry.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        retrySaga.MarkProcessing();

        db.LicenseExtensionRequests.Add(retry);
        db.LicenseExtensionSagas.Add(retrySaga);
        await db.SaveChangesAsync();

        var failedLogicalValue = failedItem.LogicalOperationId.Value;
        Assert.Equal(
            2,
            await db.SagaItemOutcomes.CountAsync(o => o.LogicalOperationId == failedItem.LogicalOperationId));

        var processor = new ProviderResultProcessor(
            db,
            new DirectSagaOrchestrator(db),
            NullLogger<ProviderResultProcessor>.Instance);

        var retryItem = retry.Items[0];
        var envelope = MessageEnvelope.Create(
            MessageTypes.ProviderLicenseExtensionCompleted,
            new ProviderLicenseExtensionCompletedV1(
                retry.Id.Value,
                retryItem.Id.Value,
                retryItem.EntitlementId.Value,
                "CloudShield",
                retryItem.LogicalOperationId.Value,
                retryItem.AttemptId.Value,
                Baseline,
                Baseline.AddDays(30),
                AlreadyApplied: false),
            MessageId.New().Value,
            requestId: retry.Id.Value,
            logicalOperationId: retryItem.LogicalOperationId.Value);

        await processor.ProcessCompletedAsync(
            envelope,
            envelope.DeserializePayload<ProviderLicenseExtensionCompletedV1>());

        await using var verify = CreateDbContext();
        var loaded = await verify.LicenseExtensionSagas
            .Include(s => s.ItemOutcomes)
            .SingleAsync(s => s.RequestId == retry.Id);
        Assert.Equal(SagaState.Completed, loaded.State);
        Assert.Equal(
            SagaItemOutcomeStatus.Succeeded,
            loaded.ItemOutcomes.Single().Status);

        Assert.Equal(
            2,
            await verify.FulfillmentItemResults.CountAsync(r =>
                r.LogicalOperationId == failedLogicalValue));
    }

    [DockerFact]
    public async Task RetryClaim_Released_WhenPriorChildIsFailed()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var sourceId = RequestId.New();
        var logical = LogicalOperationId.New();
        var firstRetry = await SeedTerminalRetryRequestAsync(
            db,
            sourceId,
            LicenseExtensionRequestStatus.Failed);

        var store = new EfRetryClaimStore(db);
        Assert.True(await store.TryClaimItemsAsync(
            sourceId, firstRetry, "key-1", [logical], null));
        await db.SaveChangesAsync();

        var secondRetry = RequestId.New();
        Assert.True(await store.TryClaimItemsAsync(
            sourceId, secondRetry, "key-2", [logical], null));
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.RetryItemClaims.CountAsync());
        Assert.Equal(
            secondRetry,
            (await db.RetryItemClaims.SingleAsync()).RetryRequestId);
    }

    [DockerFact]
    public async Task RetryClaim_Blocks_WhenPriorChildStillInFlight()
    {
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();

        var sourceId = RequestId.New();
        var logical = LogicalOperationId.New();
        var firstRetry = await SeedTerminalRetryRequestAsync(
            db,
            sourceId,
            LicenseExtensionRequestStatus.InFulfillment);

        var store = new EfRetryClaimStore(db);
        Assert.True(await store.TryClaimItemsAsync(
            sourceId, firstRetry, "key-inflight-1", [logical], null));
        await db.SaveChangesAsync();

        Assert.False(await store.TryClaimItemsAsync(
            sourceId, RequestId.New(), "key-inflight-2", [logical], null));
    }

    private async Task<(LicenseExtensionRequest Parent, LicenseExtensionSaga Saga, LicenseExtensionRequestItem FailedItem)>
        SeedFailedParentAsync(EntitleXDbContext db)
    {
        var parent = LicenseExtensionRequest.CreateFresh(
            "EXT-PARENT-1",
            "rep@entitlex.local",
            [
                (EntitlementId.From("ent-ok"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(10)),
                (EntitlementId.From("ent-fail"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(10))
            ]);
        parent.Submit();
        parent.MarkPendingApproval();
        parent.RecordApproval(ApprovedScopeHasher.Compute(parent));
        parent.BeginFulfillment();
        parent.CompleteFulfillment(succeededCount: 1, failedCount: 1);

        var saga = LicenseExtensionSaga.Start(parent.Id, ApprovalBand.AutoApprove);
        saga.QueueFulfillment(parent.Items.Select(i => (i.Id, i.EntitlementId, i.LogicalOperationId)));
        saga.MarkProcessing();
        saga.RecordItemSucceeded(parent.Items[0].LogicalOperationId);
        saga.RecordItemFailed(parent.Items[1].LogicalOperationId, "E", "fail");

        db.LicenseExtensionRequests.Add(parent);
        db.LicenseExtensionSagas.Add(saga);

        db.FulfillmentItemResults.Add(new FulfillmentItemResult(
            parent.Id.Value,
            parent.Items[1].Id.Value,
            parent.Items[1].LogicalOperationId.Value,
            parent.Items[1].AttemptId.Value,
            parent.Items[1].EntitlementId.Value,
            "CloudShield",
            succeeded: false,
            "E",
            "fail",
            DateTimeOffset.UtcNow));

        await db.SaveChangesAsync();
        return (parent, saga, parent.Items[1]);
    }

    private async Task<RequestId> SeedTerminalRetryRequestAsync(
        EntitleXDbContext db,
        RequestId _sourceIdUnused,
        LicenseExtensionRequestStatus status)
    {
        // Claim store keys by RetryRequestId status only — parent lineage is not required here.
        var request = LicenseExtensionRequest.CreateFresh(
            $"EXT-{Guid.NewGuid():N}"[..20],
            "rep@entitlex.local",
            [(EntitlementId.From("ent-x"), ProviderSystem.CloudShield, Baseline, Baseline.AddDays(5))]);
        request.Submit();

        // Drive to the requested status via allowed transitions.
        if (status is LicenseExtensionRequestStatus.Failed
            or LicenseExtensionRequestStatus.InFulfillment
            or LicenseExtensionRequestStatus.PartiallyFulfilled
            or LicenseExtensionRequestStatus.Fulfilled)
        {
            request.MarkPendingApproval();
            request.RecordApproval("hash");
            request.BeginFulfillment();
            if (status == LicenseExtensionRequestStatus.Failed)
            {
                request.CompleteFulfillment(0, 1);
            }
            else if (status == LicenseExtensionRequestStatus.Fulfilled)
            {
                request.CompleteFulfillment(1, 0);
            }
            else if (status == LicenseExtensionRequestStatus.PartiallyFulfilled)
            {
                request.CompleteFulfillment(1, 1);
            }
        }
        else if (status == LicenseExtensionRequestStatus.Rejected)
        {
            request.MarkPendingApproval();
            request.Reject();
        }

        db.LicenseExtensionRequests.Add(request);
        await db.SaveChangesAsync();
        Assert.Equal(status, request.Status);
        return request.Id;
    }

    private EntitleXDbContext CreateDbContext()
    {
        Assert.NotNull(_container);
        var options = new DbContextOptionsBuilder<EntitleXDbContext>()
            .UseNpgsql(_container!.GetConnectionString(), npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(EntitleXDbContext).Assembly.FullName);
            })
            .Options;
        return new EntitleXDbContext(options);
    }

    /// <summary>Minimal orchestrator that mutates the tracked saga in the same DbContext.</summary>
    private sealed class DirectSagaOrchestrator(EntitleXDbContext db) : ILicenseExtensionSagaOrchestrator
    {
        public Task<LicenseExtensionSaga> StartForRequestAsync(
            LicenseExtensionRequest request,
            ApprovalBand band,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task OnApprovalDecisionAsync(
            LicenseExtensionRequest request,
            ApprovalCase approvalCase,
            ApprovalDecision decision,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task OnRejectedWithoutDecisionAsync(
            LicenseExtensionRequest request,
            string reason,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();


        public Task DispatchFulfillmentAsync(
            LicenseExtensionRequest request,
            LicenseExtensionSaga saga,
            Guid? correlationId = null,
            Guid? causationId = null,
            CancellationToken cancellationToken = default) =>
            DispatchFulfillmentAsync(request, correlationId, causationId, cancellationToken);

        public Task DispatchFulfillmentAsync(
            LicenseExtensionRequest request,
            Guid? correlationId = null,
            Guid? causationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task OnFulfillmentDispatchedAsync(
            RequestId requestId,
            string? correlationId = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task OnItemCompletedAsync(
            RequestId requestId,
            LogicalOperationId logicalOperationId,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            var saga = await db.LicenseExtensionSagas
                .Include(s => s.ItemOutcomes)
                .SingleAsync(s => s.RequestId == requestId, cancellationToken);
            saga.RecordItemSucceeded(logicalOperationId, correlationId);
        }

        public async Task OnItemFailedAsync(
            RequestId requestId,
            LogicalOperationId logicalOperationId,
            string? errorCode,
            string? errorMessage,
            string? correlationId = null,
            CancellationToken cancellationToken = default)
        {
            var saga = await db.LicenseExtensionSagas
                .Include(s => s.ItemOutcomes)
                .SingleAsync(s => s.RequestId == requestId, cancellationToken);
            saga.RecordItemFailed(logicalOperationId, errorCode, errorMessage, correlationId);
        }

        public Task ApplyElapsedTimeoutsAsync(
            DateTimeOffset nowUtc,
            int take = 50,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
