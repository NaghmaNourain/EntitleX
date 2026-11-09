using EntitleX.BuildingBlocks.Ids;
using EntitleX.Domain.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class LicenseExtensionSagaConfiguration : IEntityTypeConfiguration<LicenseExtensionSaga>
{
    public void Configure(EntityTypeBuilder<LicenseExtensionSaga> builder)
    {
        builder.ToTable("license_extension_sagas");

        builder.Ignore(x => x.DomainEvents);
        builder.Ignore(x => x.SucceededItemCount);
        builder.Ignore(x => x.FailedItemCount);
        builder.Ignore(x => x.PendingItemCount);
        builder.Ignore(x => x.AllItemsTerminal);

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.RequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("request_id")
            .IsRequired();

        builder.HasIndex(x => x.RequestId)
            .IsUnique()
            .HasDatabaseName("ux_license_extension_sagas_request_id");

        builder.Property(x => x.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(x => x.State)
            .HasDatabaseName("ix_license_extension_sagas_state");

        // Reconciliation backlog / ops scans order by UpdatedAtUtc within state.
        builder.HasIndex(x => new { x.State, x.UpdatedAtUtc })
            .HasDatabaseName("ix_license_extension_sagas_state_updated");

        builder.Property(x => x.ApprovalBand)
            .HasColumnName("approval_band")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.RequiresDealDesk)
            .HasColumnName("requires_deal_desk")
            .IsRequired();

        builder.Property(x => x.RenewalManagerApproved)
            .HasColumnName("renewal_manager_approved")
            .IsRequired();

        builder.Property(x => x.DealDeskApproved)
            .HasColumnName("deal_desk_approved")
            .IsRequired();

        builder.Property(x => x.TimeoutKind)
            .HasColumnName("timeout_kind")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.TimeoutDeadlineUtc)
            .HasColumnName("timeout_deadline_utc");

        builder.HasIndex(x => x.TimeoutDeadlineUtc)
            .HasDatabaseName("ix_license_extension_sagas_timeout_deadline_utc");

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc)
            .HasColumnName("updated_at_utc")
            .IsRequired();

        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .IsConcurrencyToken()
            .HasDefaultValue(0u)
            .IsRequired();

        builder.Navigation(x => x.Transitions)
            .HasField("_transitions")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Transitions)
            .WithOne()
            .HasForeignKey("SagaId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Navigation(x => x.ItemOutcomes)
            .HasField("_itemOutcomes")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.ItemOutcomes)
            .WithOne()
            .HasForeignKey("SagaId")
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.Metadata.FindNavigation(nameof(LicenseExtensionSaga.Transitions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(LicenseExtensionSaga.ItemOutcomes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
