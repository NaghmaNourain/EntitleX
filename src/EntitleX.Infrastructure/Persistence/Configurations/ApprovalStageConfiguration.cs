using EntitleX.Domain.Approval;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ApprovalStageConfiguration : IEntityTypeConfiguration<ApprovalStage>
{
    public void Configure(EntityTypeBuilder<ApprovalStage> builder)
    {
        builder.ToTable("approval_stages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<Guid>("ApprovalCaseId")
            .HasColumnName("approval_case_id");

        builder.Property(x => x.Order)
            .HasColumnName("stage_order")
            .IsRequired();

        builder.Property(x => x.RequiredRole)
            .HasColumnName("required_role")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.DecisionId)
            .HasColumnName("decision_id");

        builder.HasIndex("ApprovalCaseId", nameof(ApprovalStage.Order))
            .IsUnique()
            .HasDatabaseName("ux_approval_stages_case_order");
    }
}
