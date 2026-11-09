using EntitleX.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class HttpWriteIdempotencyRecordConfiguration
    : IEntityTypeConfiguration<HttpWriteIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<HttpWriteIdempotencyRecord> builder)
    {
        builder.ToTable("http_write_idempotency_records");

        builder.HasKey(x => x.IdempotencyKey);

        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.Operation)
            .HasColumnName("operation")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.RequestFingerprint)
            .HasColumnName("request_fingerprint")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ResourceId)
            .HasColumnName("resource_id")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(x => new { x.Operation, x.ActorUserId })
            .HasDatabaseName("ix_http_write_idempotency_operation_actor");
    }
}
