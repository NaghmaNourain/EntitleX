using EntitleX.Domain.Saga;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EntitleX.Infrastructure.Persistence.Configurations;

public sealed class SagaTransitionConfiguration : IEntityTypeConfiguration<SagaTransition>
{
    public void Configure(EntityTypeBuilder<SagaTransition> builder)
    {
        builder.ToTable("saga_transitions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(x => x.FromState)
            .HasColumnName("from_state")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ToState)
            .HasColumnName("to_state")
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(x => x.Actor)
            .HasColumnName("actor")
            .HasMaxLength(256);

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128);

        builder.Property(x => x.OccurredAtUtc)
            .HasColumnName("occurred_at_utc")
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnName("metadata")
            .HasMaxLength(1024);

        builder.HasIndex("SagaId")
            .HasDatabaseName("ix_saga_transitions_saga_id");

        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("ix_saga_transitions_occurred_at_utc");
    }
}
