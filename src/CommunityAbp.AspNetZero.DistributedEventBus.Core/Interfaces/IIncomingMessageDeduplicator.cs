using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>Optional application-owned duplicate suppression for at-least-once transports.</summary>
public interface IIncomingMessageDeduplicator
{
    /// <summary>
    /// Attempts to acquire processing ownership for a broker message. A message that is
    /// already completed can be broker-completed; a message currently held by another
    /// processor must be returned to the broker for redelivery instead.
    /// </summary>
    Task<IncomingMessageDeduplicationResult> TryAcquireAsync(DistributedEventMessageContext context, CancellationToken cancellationToken = default);
    Task CompleteAsync(DistributedEventMessageContext context, CancellationToken cancellationToken = default);
    Task AbandonAsync(DistributedEventMessageContext context, CancellationToken cancellationToken = default);
}
