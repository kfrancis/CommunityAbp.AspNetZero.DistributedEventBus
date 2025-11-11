using Microsoft.EntityFrameworkCore;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;

/// <summary>
/// Provides model configuration for distributed event bus inbox/outbox entities.
/// Call from your production DbContext so its migrations include these tables.
/// </summary>
public static class DistributedEventBusModelBuilderExtensions
{
    /// <summary>
    /// Configures the distributed event bus entities.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="schema">Optional schema name to place tables under.</param>
    public static void ConfigureDistributedEventBus(this ModelBuilder modelBuilder, string? schema = null)
    {
        var inbox = modelBuilder.Entity<InboxMessage>();
        if (!string.IsNullOrWhiteSpace(schema))
        {
            inbox.ToTable("InboxMessages", schema);
        }
        // Unique constraint on MessageId ensures idempotency.
        inbox.HasIndex(x => x.MessageId).IsUnique();

        var outbox = modelBuilder.Entity<OutboxMessage>();
        if (!string.IsNullOrWhiteSpace(schema))
        {
            outbox.ToTable("OutboxMessages", schema);
        }
    }
}
