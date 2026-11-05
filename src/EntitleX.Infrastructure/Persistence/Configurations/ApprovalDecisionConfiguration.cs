using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ApprovalDecisionConfiguration : IEntityTypeConfiguration<ApprovalDecision>
{
    public void Configure(EntityTypeBuilder<ApprovalDecision> builder)
    {
        builder.ToTable("approval_decisions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<Guid>("ApprovalCaseId")
            .HasColumnName("approval_case_id");

        builder.Property(x => x.RequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("request_id")
            .IsRequired();

        builder.Property(x => x.StageOrder)
            .HasColumnName("stage_order")
            .IsRequired();

        builder.Property(x => x.StageRole)
            .HasColumnName("stage_role")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ActorDisplayName)
            .HasColumnName("actor_display_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Comment)
            .HasColumnName("comment")
            .HasMaxLength(2000);

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128);

        builder.Property(x => x.DecidedAtUtc)
            .HasColumnName("decided_at_utc")
            .IsRequired();

        builder.HasIndex(x => x.RequestId)
            .HasDatabaseName("ix_approval_decisions_request_id");

        builder.HasIndex(x => x.DecidedAtUtc)
            .HasDatabaseName("ix_approval_decisions_decided_at_utc");
    }
}
