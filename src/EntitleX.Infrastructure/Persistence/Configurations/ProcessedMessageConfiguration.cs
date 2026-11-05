using EntitleX.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");

        builder.HasKey(x => new { x.ConsumerName, x.IdempotencyKey });

        builder.Property(x => x.ConsumerName).HasColumnName("consumer_name").HasMaxLength(128);
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(256);
        builder.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(x => x.MessageType).HasColumnName("message_type").HasMaxLength(200);
    }
}
