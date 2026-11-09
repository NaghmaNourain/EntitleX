using EntitleX.SharedKernel.Ids;
using EntitleX.Domain.LicenseExtension;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class RetryHistoryEntryConfiguration : IEntityTypeConfiguration<RetryHistoryEntry>
{
    public void Configure(EntityTypeBuilder<RetryHistoryEntry> builder)
    {
        builder.ToTable("retry_history");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.SourceRequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("source_request_id")
            .IsRequired();

        builder.Property(x => x.RetryRequestId)
            .HasConversion(id => id.Value, value => RequestId.From(value))
            .HasColumnName("retry_request_id")
            .IsRequired();

        builder.Property(x => x.ExtensionNumber)
            .HasColumnName("extension_number")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ItemCount)
            .HasColumnName("item_count")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Notes)
            .HasColumnName("notes")
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAtUtc)
            .HasColumnName("created_at_utc")
            .IsRequired();

        builder.HasIndex(x => x.SourceRequestId)
            .HasDatabaseName("ix_retry_history_source_request_id");

        builder.HasIndex(x => x.RetryRequestId)
            .IsUnique()
            .HasDatabaseName("ux_retry_history_retry_request_id");
    }
}
