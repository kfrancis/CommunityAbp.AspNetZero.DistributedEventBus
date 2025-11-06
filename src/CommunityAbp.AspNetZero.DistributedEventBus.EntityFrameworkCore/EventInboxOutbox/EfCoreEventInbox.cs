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
            CorrelationId = eventInfo.GetCorrelationId()
        };
        _dbContext.InboxMessages.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IncomingEventInfo>> GetPendingAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.InboxMessages
            .Where(x => x.Status == "Pending")
            .OrderBy(x => x.ReceivedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new IncomingEventInfo(e.Id, e.MessageId, e.EventName, e.EventData, e.ReceivedAt).SetCorrelationId(e.CorrelationId ?? string.Empty)).ToList();
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.InboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity != null)
        {
            entity.Status = "Processed";
            entity.ProcessedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.InboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity != null)
        {
            entity.Status = "Failed";
            entity.Error = reason;
            entity.RetryCount += 1;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
