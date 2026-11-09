using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class SagaItemOutcomeConfiguration : IEntityTypeConfiguration<SagaItemOutcome>
{
    public void Configure(EntityTypeBuilder<SagaItemOutcome> builder)
    {
        builder.ToTable("saga_item_outcomes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.RequestItemId)
            .HasConversion(id => id.Value, value => RequestItemId.From(value))
            .HasColumnName("request_item_id")
            .IsRequired();

        builder.Property(x => x.EntitlementId)
            .HasConversion(id => id.Value, value => EntitlementId.From(value))
            .HasColumnName("entitlement_id")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.LogicalOperationId)
            .HasConversion(id => id.Value, value => LogicalOperationId.From(value))
            .HasColumnName("logical_operation_id")
            .IsRequired();

        // Scoped per saga so selective retries (same LogicalOperationId, new saga) can seed outcomes.
        builder.HasIndex("SagaId", nameof(SagaItemOutcome.LogicalOperationId))
            .IsUnique()
            .HasDatabaseName("ux_saga_item_outcomes_saga_logical_operation_id");

        builder.HasIndex(x => x.LogicalOperationId)
            .HasDatabaseName("ix_saga_item_outcomes_logical_operation_id");

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(128);

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(1024);

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.HasIndex("SagaId")
            .HasDatabaseName("ix_saga_item_outcomes_saga_id");
    }
}
