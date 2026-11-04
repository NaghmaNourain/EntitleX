using EntitleX.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.MessageId).HasColumnName("message_id").IsRequired();
        builder.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.DestinationKind).HasColumnName("destination_kind").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Destination).HasColumnName("destination").HasMaxLength(256).IsRequired();
        builder.Property(x => x.PartitionKey).HasColumnName("partition_key").HasMaxLength(256);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.AvailableAtUtc).HasColumnName("available_at_utc");
        builder.Property(x => x.LockedUntilUtc).HasColumnName("locked_until_utc");
        builder.Property(x => x.LockedBy).HasColumnName("locked_by").HasMaxLength(128);
        builder.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.CausationId).HasColumnName("causation_id");
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.Property(x => x.LogicalOperationId).HasColumnName("logical_operation_id");
        builder.Property(x => x.SchemaVersion).HasColumnName("schema_version").HasMaxLength(32).IsRequired();

        builder.HasIndex(x => x.MessageId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.AvailableAtUtc })
            .HasDatabaseName("ix_outbox_messages_status_available");
    }
}
