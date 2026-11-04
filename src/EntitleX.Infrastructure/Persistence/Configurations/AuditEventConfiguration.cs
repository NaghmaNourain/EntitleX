using EntitleX.BuildingBlocks.Ids;
using EntitleX.Infrastructure.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.EventType)
            .HasColumnName("event_type")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.RequestId)
            .HasColumnName("request_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? RequestId.From(value.Value) : null);

        builder.Property(x => x.LogicalOperationId)
            .HasColumnName("logical_operation_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? LogicalOperationId.From(value.Value) : null);

        builder.Property(x => x.Actor)
            .HasColumnName("actor")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Detail)
            .HasColumnName("detail")
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128);

        builder.Property(x => x.OccurredAtUtc)
            .HasColumnName("occurred_at_utc");

        builder.HasIndex(x => x.RequestId);
        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.OccurredAtUtc);
    }
}
