using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Abp.Dependency; // add marker

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;


public class EfCoreEventOutbox : IEventOutbox, ITransientDependency
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
            .AsNoTracking()
            .Where(x => x.Status == "Pending")
            .OrderBy(x => x.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new OutgoingEventInfo(e.Id, e.EventName, e.EventData, e.CreatedAt).SetCorrelationId(e.CorrelationId ?? string.Empty)).ToList();
    }

    public async Task<bool> TryClaimAsync(object id, CancellationToken cancellationToken)
    {
        if (id is not Guid guid)
        {
            throw new ArgumentException("id must be a Guid", nameof(id));
        }

        var affected = await _dbContext.OutboxMessages
            .Where(x => x.Id == guid && x.Status == "Pending")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Processing")
                .SetProperty(x => x.ProcessingStartedAt, DateTime.UtcNow), cancellationToken);

        return affected == 1;
    }

    public Task<int> RequeueExpiredClaimsAsync(TimeSpan leaseTimeout, CancellationToken cancellationToken)
    {
        var expiredBefore = DateTime.UtcNow - leaseTimeout;
        return _dbContext.OutboxMessages
            .Where(x => x.Status == "Processing" && x.ProcessingStartedAt < expiredBefore)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Pending")
                .SetProperty(x => x.ProcessingStartedAt, (DateTime?)null), cancellationToken);
    }

    public async Task MarkSentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _dbContext.OutboxMessages
            .Where(x => x.Id == id && x.Status == "Processing")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Sent")
                .SetProperty(x => x.SentAt, DateTime.UtcNow)
                .SetProperty(x => x.Error, (string?)null)
                .SetProperty(x => x.ProcessingStartedAt, (DateTime?)null), cancellationToken);
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
        await _dbContext.OutboxMessages
            .Where(x => x.Id == id && x.Status == "Processing")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, "Failed")
                .SetProperty(x => x.Error, reason)
                .SetProperty(x => x.RetryCount, x => x.RetryCount + 1)
                .SetProperty(x => x.ProcessingStartedAt, (DateTime?)null), cancellationToken);
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
        return _dbContext.OutboxMessages
            .AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(1000)
            .Select(e => new OutgoingEventInfo(e.Id, e.EventName, e.EventData, e.CreatedAt).SetCorrelationId(e.CorrelationId ?? string.Empty))
            .ToList();
    }
}
