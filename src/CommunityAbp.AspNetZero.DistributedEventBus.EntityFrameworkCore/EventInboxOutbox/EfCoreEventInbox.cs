using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

public class EfCoreEventInbox : IEventInbox
{
    private readonly DistributedEventBusDbContext _dbContext;

    public EfCoreEventInbox(DistributedEventBusDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(IncomingEventInfo eventInfo, CancellationToken cancellationToken = default)
    {
        var entity = new InboxMessage
        {
            Id = eventInfo.Id,
            MessageId = eventInfo.MessageId,
            EventName = eventInfo.EventName,
            EventType = eventInfo.EventName,
            EventData = eventInfo.EventData,
            ReceivedAt = eventInfo.CreationTime,
            Status = "Pending",
            CorrelationId = eventInfo.GetCorrelationId(),
            LegacyTypeIdentifier = eventInfo.MessageContext?.LegacyTypeIdentifier,
            EntityPath = eventInfo.MessageContext?.EntityPath,
            SubscriptionName = eventInfo.MessageContext?.SubscriptionName,
            DeliveryCount = eventInfo.MessageContext?.DeliveryCount,
            DispatchMode = (int)(eventInfo.MessageContext?.DispatchMode ?? DistributedEventDispatchMode.Direct)
        };
        _dbContext.InboxMessages.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingEventInfo>> GetPendingAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.InboxMessages
            .AsNoTracking()
            .Where(x => x.Status == "Pending")
            .OrderBy(x => x.ReceivedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new IncomingEventInfo(e.Id, e.MessageId, e.EventName, e.EventData, e.ReceivedAt)
            .SetCorrelationId(e.CorrelationId ?? string.Empty)
            .SetMessageContext(new DistributedEventMessageContext
            {
                MessageId = e.MessageId,
                EventName = e.EventName,
                LegacyTypeIdentifier = e.LegacyTypeIdentifier,
                EntityPath = e.EntityPath,
                SubscriptionName = e.SubscriptionName,
                DeliveryCount = e.DeliveryCount,
                CorrelationId = e.CorrelationId,
                DispatchMode = (DistributedEventDispatchMode)e.DispatchMode,
                CreatedAtUtc = e.ReceivedAt
            })).ToList();
    }

    public async Task<bool> TryClaimAsync(Guid id, CancellationToken cancellationToken)
    {
        var affected = await _dbContext.InboxMessages
            .Where(x => x.Id == id && x.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Processing")
                .SetProperty(x => x.ProcessingStartedAt, DateTime.UtcNow), cancellationToken);

        return affected == 1;
    }

    public Task<int> RequeueExpiredClaimsAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken)
    {
        var expiredBefore = DateTime.UtcNow - leaseTimeout;
        return _dbContext.InboxMessages
            .Where(x => x.Status == "Processing" && x.ProcessingStartedAt < expiredBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Pending")
                .SetProperty(x => x.ProcessingStartedAt, (DateTime?)null), cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _dbContext.InboxMessages
            .Where(x => x.Id == id && x.Status == "Processing")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Processed")
                .SetProperty(x => x.ProcessedAt, DateTime.UtcNow)
                .SetProperty(x => x.Error, (string?)null)
                .SetProperty(x => x.ProcessingStartedAt, (DateTime?)null), cancellationToken);
    }

    public async Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        await _dbContext.InboxMessages
            .Where(x => x.Id == id && x.Status == "Processing")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Failed")
                .SetProperty(x => x.Error, reason)
                .SetProperty(x => x.RetryCount, x => x.RetryCount + 1)
                .SetProperty(x => x.ProcessingStartedAt, (DateTime?)null), cancellationToken);
    }
}
