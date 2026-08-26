using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>
/// Persists incoming distributed events (Inbox pattern).
/// </summary>
// Add this interface method to IEventInbox if it is missing
public interface IEventInbox
{
    Task AddAsync(IncomingEventInfo incomingEvent, CancellationToken cancellationToken = default);
    // Add the following method signature:
    Task<IReadOnlyList<IncomingEventInfo>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default);
    Task<bool> TryClaimAsync(Guid id, CancellationToken ct);
    Task<int> RequeueExpiredClaimsAsync(TimeSpan leaseTimeout, CancellationToken ct);
    Task MarkFailedAsync(Guid id, string v, CancellationToken ct);
    Task MarkProcessedAsync(Guid id, CancellationToken ct);
}
