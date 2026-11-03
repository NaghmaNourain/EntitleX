using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.LicenseExtension;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class LicenseExtensionRequestConfiguration : IEntityTypeConfiguration<LicenseExtensionRequest>
{
    public void Configure(EntityTypeBuilder<LicenseExtensionRequest> builder)
    {
        builder.ToTable("license_extension_requests");

        builder.Ignore(x => x.DomainEvents);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.ExtensionNumber)
            .HasColumnName("extension_number")
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.ExtensionNumber)
            .IsUnique()
            .HasDatabaseName("ux_license_extension_requests_extension_number");

        builder.Property(x => x.ParentId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? RequestId.From(value.Value) : null)
            .HasColumnName("parent_id");

        builder.HasIndex(x => x.ParentId)
            .HasDatabaseName("ix_license_extension_requests_parent_id");

        builder.HasOne<LicenseExtensionRequest>()
            .WithMany()
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ApprovalVersion)
            .HasColumnName("approval_version")
            .IsRequired();

        builder.Property(x => x.ApprovedScopeHash)
            .HasColumnName("approved_scope_hash")
            .HasMaxLength(128);

        builder.Property(x => x.RequestedBy)
            .HasColumnName("requested_by")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .IsConcurrencyToken()
            .HasDefaultValue(0u)
            .IsRequired();

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_license_extension_requests_status");

        builder.HasIndex(x => x.CreatedAtUtc)
            .HasDatabaseName("ix_license_extension_requests_created_at_utc");

        builder.Navigation(x => x.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey("RequestId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(LicenseExtensionRequest.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
