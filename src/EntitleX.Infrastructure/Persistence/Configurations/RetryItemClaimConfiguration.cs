using EntitleX.BuildingBlocks.Ids;
using EntitleX.Infrastructure.Retry;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class RetryItemClaimConfiguration : IEntityTypeConfiguration<RetryItemClaim>
{
    public void Configure(EntityTypeBuilder<RetryItemClaim> builder)
    {
        builder.ToTable("retry_item_claims");

        builder.HasKey(x => new { x.SourceRequestId, x.LogicalOperationId });

        builder.Property(x => x.SourceRequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("source_request_id")
            .IsRequired();

        builder.Property(x => x.LogicalOperationId)
            .HasConversion(id => id.Value, value => LogicalOperationId.From(value))
            .HasColumnName("logical_operation_id")
            .IsRequired();

        builder.Property(x => x.RetryRequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("retry_request_id")
            .IsRequired();

        builder.Property(x => x.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.ClaimedAtUtc)
            .HasColumnName("claimed_at_utc")
            .IsRequired();

        builder.HasIndex(x => x.RetryRequestId)
            .HasDatabaseName("ix_retry_item_claims_retry_request_id");

        builder.HasIndex(x => x.IdempotencyKey)
            .HasDatabaseName("ix_retry_item_claims_idempotency_key");
    }
}
