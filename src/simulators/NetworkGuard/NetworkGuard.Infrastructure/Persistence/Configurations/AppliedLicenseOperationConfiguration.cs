using EntitleX.BuildingBlocks.Ids;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkGuard.Domain.Operations;

namespace NetworkGuard.Infrastructure.Persistence.Configurations;

public sealed class AppliedLicenseOperationConfiguration
    : IEntityTypeConfiguration<AppliedLicenseOperation>
{
    public void Configure(EntityTypeBuilder<AppliedLicenseOperation> builder)
    {
        builder.ToTable("applied_license_operations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.LogicalOperationId)
            .HasConversion(id => id.Value, value => LogicalOperationId.From(value))
            .HasColumnName("logical_operation_id")
            .IsRequired();

        builder.HasIndex(x => x.LogicalOperationId)
            .IsUnique()
            .HasDatabaseName("ux_applied_license_operations_logical_operation_id");

        builder.Property(x => x.AttemptId)
            .HasConversion(id => id.Value, value => AttemptId.From(value))
            .HasColumnName("attempt_id")
            .IsRequired();

        builder.Property(x => x.LicenseId)
            .HasColumnName("license_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.LicenseId)
            .HasDatabaseName("ix_applied_license_operations_license_id");

        builder.Property(x => x.PreviousExpiryUtc)
            .HasColumnName("previous_expiry_utc")
            .IsRequired();

        builder.Property(x => x.NewExpiryUtc)
            .HasColumnName("new_expiry_utc")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(x => x.CompletedAtUtc)
            .HasColumnName("completed_at_utc");

        builder.Property(x => x.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(1024);
    }
}
