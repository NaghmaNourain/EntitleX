using EntitleX.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ExpirationNotificationRecordConfiguration
    : IEntityTypeConfiguration<ExpirationNotificationRecord>
{
    public void Configure(EntityTypeBuilder<ExpirationNotificationRecord> builder)
    {
        builder.ToTable("expiration_notification_records");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.EntitlementId)
            .HasColumnName("entitlement_id")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.CustomerId)
            .HasColumnName("customer_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.WindowKey)
            .HasColumnName("window_key")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.ExpiryUtc).HasColumnName("expiry_utc");
        builder.Property(x => x.NotifiedAtUtc).HasColumnName("notified_at_utc");

        builder.HasIndex(x => x.WindowKey)
            .IsUnique()
            .HasDatabaseName("ux_expiration_notification_records_window_key");

        builder.HasIndex(x => new { x.Provider, x.EntitlementId })
            .HasDatabaseName("ix_expiration_notification_records_provider_entitlement");
    }
}
