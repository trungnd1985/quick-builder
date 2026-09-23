using Microsoft.EntityFrameworkCore;

namespace QuickBuilder.Shared.Infrastructure.Outbox;

/// <summary>
/// Maps the outbox tables into a module's own schema (D-01).
/// </summary>
/// <remarks>
/// Every module calls this from OnModelCreating. The tables live in the module's schema,
/// which is what keeps FR-015's atomicity honest: one DbContext, one transaction, one
/// SaveChanges.
/// </remarks>
public static class OutboxModelBuilder
{
    public static void AddOutbox(this ModelBuilder modelBuilder, string schema)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages", schema);
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.Property(e => e.TraceId).HasMaxLength(64);

            // The claim query's only index. Partial, so completed rows drop out of it and
            // it stays small no matter how much history accumulates before the retention
            // sweep removes it.
            entity
                .HasIndex(e => e.AvailableAtUtc)
                .HasDatabaseName("ix_outbox_messages_pending")
                .HasFilter("completed_at_utc IS NULL AND failed_at_utc IS NULL");

            // For the retention sweep.
            entity
                .HasIndex(e => e.CompletedAtUtc)
                .HasDatabaseName("ix_outbox_messages_completed")
                .HasFilter("completed_at_utc IS NOT NULL");
        });

        modelBuilder.Entity<ProcessedEvent>(entity =>
        {
            entity.ToTable("processed_events", schema);

            // Composite key: one event may have several handlers, each deduplicated
            // independently.
            entity.HasKey(e => new { e.EventId, e.HandlerName });
            entity.Property(e => e.HandlerName).HasMaxLength(300);
        });
    }
}
