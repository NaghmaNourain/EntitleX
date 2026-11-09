using EntitleX.SharedKernel.Ids;
using EntitleX.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ApprovalReminderRecordConfiguration : IEntityTypeConfiguration<ApprovalReminderRecord>
{
    public void Configure(EntityTypeBuilder<ApprovalReminderRecord> builder)
    {
        builder.ToTable("approval_reminder_records");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ApprovalCaseId).HasColumnName("approval_case_id");

        builder.Property(x => x.RequestId)
            .HasColumnName("request_id")
            .HasConversion(id => id.Value, value => RequestId.From(value));

        builder.Property(x => x.StageOrder).HasColumnName("stage_order");

        builder.Property(x => x.WindowKey)
            .HasColumnName("window_key")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ReminderSentAtUtc).HasColumnName("reminder_sent_at_utc");
        builder.Property(x => x.EscalatedAtUtc).HasColumnName("escalated_at_utc");

        builder.Property(x => x.TerminalStateApplied)
            .HasColumnName("terminal_state_applied")
            .HasMaxLength(64);

        builder.HasIndex(x => x.WindowKey)
            .IsUnique()
            .HasDatabaseName("ux_approval_reminder_records_window_key");

        builder.HasIndex(x => new { x.ApprovalCaseId, x.StageOrder })
            .IsUnique()
            .HasDatabaseName("ux_approval_reminder_records_case_stage");
    }
}
