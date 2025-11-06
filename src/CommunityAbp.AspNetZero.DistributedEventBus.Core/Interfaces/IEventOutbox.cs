using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>
///     Abstraction for persisting outgoing events (Transactional Outbox).
/// </summary>
public interface IEventOutbox
{
    Task AddAsync(OutgoingEventInfo outgoingEvent, CancellationToken cancellationToken = default);
    IReadOnlyList<OutgoingEventInfo> GetEvents();
    Task<IEnumerable<OutgoingEventInfo>> GetPendingAsync(int outboxBatchSize, CancellationToken ct);
    Task MarkFailedAsync(object id, string v, CancellationToken ct);
    Task MarkSentAsync(object id, CancellationToken ct);
}
