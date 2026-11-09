using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class LicenseExtensionRequestItemConfiguration
    : IEntityTypeConfiguration<LicenseExtensionRequestItem>
{
    public void Configure(EntityTypeBuilder<LicenseExtensionRequestItem> builder)
    {
        builder.ToTable("license_extension_request_items");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => RequestItemId.From(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property<RequestId>("RequestId")
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("request_id")
            .IsRequired();

        builder.HasIndex("RequestId")
            .HasDatabaseName("ix_license_extension_request_items_request_id");

        builder.Property(x => x.EntitlementId)
            .HasConversion(id => id.Value, value => EntitlementId.From(value))
            .HasColumnName("entitlement_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.LogicalOperationId)
            .HasConversion(id => id.Value, value => LogicalOperationId.From(value))
            .HasColumnName("logical_operation_id")
            .IsRequired();

        builder.HasIndex(x => x.LogicalOperationId)
            .HasDatabaseName("ix_license_extension_request_items_logical_operation_id");

        builder.Property(x => x.AttemptId)
            .HasConversion(id => id.Value, value => AttemptId.From(value))
            .HasColumnName("attempt_id")
            .IsRequired();

        builder.Property(x => x.CurrentExpiryUtc)
            .HasColumnName("current_expiry_utc")
            .IsRequired();

        builder.Property(x => x.RequestedExpiryUtc)
            .HasColumnName("requested_expiry_utc")
            .IsRequired();

        builder.HasIndex(x => x.EntitlementId)
            .HasDatabaseName("ix_license_extension_request_items_entitlement_id");
    }
}
