using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Approval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ApprovalCaseConfiguration : IEntityTypeConfiguration<ApprovalCase>
{
    public void Configure(EntityTypeBuilder<ApprovalCase> builder)
    {
        builder.ToTable("approval_cases");

        builder.Ignore(x => x.DomainEvents);
        builder.Ignore(x => x.CurrentStage);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.RequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("request_id")
            .IsRequired();

        builder.HasIndex(x => x.RequestId)
            .IsUnique()
            .HasDatabaseName("ux_approval_cases_request_id");

        builder.Property(x => x.MaxExtensionDays)
            .HasColumnName("max_extension_days")
            .IsRequired();

        builder.Property(x => x.Band)
            .HasColumnName("band")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.CurrentStageOrder)
            .HasColumnName("current_stage_order")
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(x => x.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Navigation(x => x.Stages)
            .HasField("_stages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Stages)
            .WithOne()
            .HasForeignKey("ApprovalCaseId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(x => x.Decisions)
            .HasField("_decisions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Decisions)
            .WithOne()
            .HasForeignKey("ApprovalCaseId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(ApprovalCase.Stages))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(ApprovalCase.Decisions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
