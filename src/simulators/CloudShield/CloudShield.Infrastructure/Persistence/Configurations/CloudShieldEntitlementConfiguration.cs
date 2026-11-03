using CloudShield.Domain.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CloudShield.Infrastructure.Persistence.Configurations;

public sealed class CloudShieldEntitlementConfiguration : IEntityTypeConfiguration<CloudShieldEntitlement>
{
    public void Configure(EntityTypeBuilder<CloudShieldEntitlement> builder)
    {
        builder.ToTable("entitlements");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.EntitlementKey)
            .HasColumnName("entitlement_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.EntitlementKey)
            .IsUnique()
            .HasDatabaseName("ux_entitlements_entitlement_key");

        builder.Property(x => x.CustomerAccountId)
            .HasColumnName("customer_account_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(x => x.CustomerAccountId)
            .HasDatabaseName("ix_entitlements_customer_account_id");

        builder.Property(x => x.ProductCode)
            .HasColumnName("product_code")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ProductGroup)
            .HasColumnName("product_group")
            .HasMaxLength(64);

        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc)
            .HasColumnName("expires_at_utc")
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();
    }
}
