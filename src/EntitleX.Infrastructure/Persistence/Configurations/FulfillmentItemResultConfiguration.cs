using EntitleX.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class FulfillmentItemResultConfiguration : IEntityTypeConfiguration<FulfillmentItemResult>
{
    public void Configure(EntityTypeBuilder<FulfillmentItemResult> builder)
    {
        builder.ToTable("fulfillment_item_results");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.RequestId).HasColumnName("request_id");
        builder.Property(x => x.RequestItemId).HasColumnName("request_item_id");
        builder.Property(x => x.LogicalOperationId).HasColumnName("logical_operation_id");
        builder.Property(x => x.EntitlementId).HasColumnName("entitlement_id").HasMaxLength(256);
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(64);
        builder.Property(x => x.Succeeded).HasColumnName("succeeded");
        builder.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(2000);
        builder.Property(x => x.RecordedAtUtc).HasColumnName("recorded_at_utc");

        builder.HasIndex(x => x.LogicalOperationId).IsUnique();
        builder.HasIndex(x => x.RequestId);
    }
}
