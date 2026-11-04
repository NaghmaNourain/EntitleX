using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NetworkGuard.Domain.Licenses;

namespace NetworkGuard.Infrastructure.Persistence.Configurations;

public sealed class NetworkGuardLicenseConfiguration : IEntityTypeConfiguration<NetworkGuardLicense>
{
    public void Configure(EntityTypeBuilder<NetworkGuardLicense> builder)
    {
        builder.ToTable("licenses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.LicenseId)
            .HasColumnName("license_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.LicenseId)
            .IsUnique()
            .HasDatabaseName("ux_licenses_license_id");

        builder.Property(x => x.TenantExternalId)
            .HasColumnName("tenant_external_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.TenantExternalId)
            .HasDatabaseName("ix_licenses_tenant_external_id");

        builder.Property(x => x.SkuCode)
            .HasColumnName("sku_code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.LicenseGroup)
            .HasColumnName("license_group")
            .HasMaxLength(64);

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ValidUntilUtc)
            .HasColumnName("valid_until_utc")
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();
    }
}
