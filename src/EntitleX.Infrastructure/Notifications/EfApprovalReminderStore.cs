using EntitleX.Application.Abstractions;
using EntitleX.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EntitleX.Infrastructure.Notifications;

public sealed class EfApprovalReminderStore : IApprovalReminderStore
{
    private readonly EntitleXDbContext _db;

    public EfApprovalReminderStore(EntitleXDbContext db)
    {
        _db = db;
    }

    public async Task<ApprovalReminderRecordDto?> GetByWindowKeyAsync(
        string windowKey,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.ApprovalReminderRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.WindowKey == windowKey, cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<ApprovalReminderRecordDto?> GetForStageAsync(
        Guid approvalCaseId,
        int stageOrder,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.ApprovalReminderRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.ApprovalCaseId == approvalCaseId && x.StageOrder == stageOrder,
                cancellationToken);

        return row is null ? null : Map(row);
    }

    public async Task<bool> TryRecordReminderAsync(
        ApprovalReminderRecordDto record,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.ApprovalReminderRecords
            .AnyAsync(x => x.WindowKey == record.WindowKey, cancellationToken);
        if (exists)
        {
            return false;
        }

        _db.ApprovalReminderRecords.Add(new ApprovalReminderRecord(
            record.Id,
            record.ApprovalCaseId,
            record.RequestId,
            record.StageOrder,
            record.WindowKey,
            record.ReminderSentAtUtc,
            record.EscalatedAtUtc,
            record.TerminalStateApplied));

        return true;
    }

    public async Task MarkEscalatedAsync(
        string windowKey,
        string terminalStateApplied,
        DateTimeOffset escalatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var row = await _db.ApprovalReminderRecords
            .SingleOrDefaultAsync(x => x.WindowKey == windowKey, cancellationToken);

        if (row is null)
        {
            return;
        }

        row.MarkEscalated(terminalStateApplied, escalatedAtUtc);
    }

    private static ApprovalReminderRecordDto Map(ApprovalReminderRecord row) =>
        new(
            row.Id,
            row.ApprovalCaseId,
            row.RequestId,
            row.StageOrder,
            row.WindowKey,
            row.ReminderSentAtUtc,
            row.EscalatedAtUtc,
            row.TerminalStateApplied);
}
