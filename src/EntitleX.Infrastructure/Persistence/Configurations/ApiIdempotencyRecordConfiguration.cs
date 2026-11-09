using EntitleX.SharedKernel.Ids;
using EntitleX.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ApiIdempotencyRecordConfiguration : IEntityTypeConfiguration<ApiIdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<ApiIdempotencyRecord> builder)
    {
        builder.ToTable("api_idempotency_records");

        builder.HasKey(x => x.IdempotencyKey);

        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.SourceRequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("source_request_id")
            .IsRequired();

        builder.Property(x => x.RetryRequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("retry_request_id")
            .IsRequired();

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(x => x.SourceRequestId)
            .HasDatabaseName("ix_api_idempotency_records_source_request_id");
    }
}
