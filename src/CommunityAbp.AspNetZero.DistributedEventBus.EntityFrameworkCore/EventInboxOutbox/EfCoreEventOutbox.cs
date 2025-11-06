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

public class EfCoreEventOutbox : IEventOutbox
{
    private readonly DistributedEventBusDbContext _dbContext;

    public EfCoreEventOutbox(DistributedEventBusDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(OutgoingEventInfo eventInfo, CancellationToken cancellationToken = default)
    {
        var entity = new OutboxMessage
        {
            Id = eventInfo.Id,
            EventName = eventInfo.EventName,
            EventType = eventInfo.EventName, // store assembly qualified in EventName already
            EventData = eventInfo.EventData,
            CreatedAt = eventInfo.CreationTime,
            Status = "Pending",
            CorrelationId = eventInfo.GetCorrelationId()
        };
        _dbContext.OutboxMessages.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<OutgoingEventInfo>> GetPendingAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        var entities = await _dbContext.OutboxMessages
            .Where(x => x.Status == "Pending")
            .OrderBy(x => x.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new OutgoingEventInfo(e.Id, e.EventName, e.EventData, e.CreatedAt).SetCorrelationId(e.CorrelationId ?? string.Empty)).ToList();
    }

    public async Task MarkSentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity != null)
        {
            entity.Status = "Sent";
            entity.SentAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkSentAsync(object id, CancellationToken cancellationToken = default)
    {
        if (id is Guid guid)
        {
            await MarkSentAsync(guid, cancellationToken);
        }
        else
        {
            throw new ArgumentException("id must be a Guid", nameof(id));
        }
    }

    public async Task MarkFailedAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var entity = await _dbContext.OutboxMessages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity != null)
        {
            entity.Status = "Failed";
            entity.Error = reason;
            entity.RetryCount += 1;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkFailedAsync(object id, string reason, CancellationToken cancellationToken = default)
    {
        if (id is Guid guid)
        {
            await MarkFailedAsync(guid, reason, cancellationToken);
        }
        else
        {
            throw new ArgumentException("id must be a Guid", nameof(id));
        }
    }

    public IReadOnlyList<OutgoingEventInfo> GetEvents()
    {
        // Return all OutboxMessages as OutgoingEventInfo.
        // This is a simple implementation; you may want to filter or page in a real scenario.
        return _dbContext.OutboxMessages
            .AsNoTracking()
            .Select(e => new OutgoingEventInfo(e.Id, e.EventName, e.EventData, e.CreatedAt).SetCorrelationId(e.CorrelationId ?? string.Empty))
            .ToList();
    }
}
