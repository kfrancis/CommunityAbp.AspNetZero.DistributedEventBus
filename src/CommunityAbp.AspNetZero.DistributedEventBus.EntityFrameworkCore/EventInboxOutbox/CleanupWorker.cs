using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Abp.Dependency;
using Microsoft.Extensions.Logging;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Abp.Threading.BackgroundWorkers;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

public interface ICleanupWorker : IBackgroundWorker { }

public class CleanupWorker : ICleanupWorker, ISingletonDependency
{
    private readonly DistributedEventBusDbContext _dbContext;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private readonly ILogger<CleanupWorker> _logger;
    private Timer? _timer;

    public CleanupWorker(DistributedEventBusDbContext dbContext, AspNetZeroEventBusBoxesOptions options, ILogger<CleanupWorker> logger)
    {
        _dbContext = dbContext;
        _options = options;
        _logger = logger;
    }

    public void Start()
    {
        _timer = new Timer(async _ => await DoWorkAsync(), null, TimeSpan.Zero, _options.CleanOldEventTimeIntervalSpan);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void WaitToStop() { }

    private async Task DoWorkAsync()
    {
        try
        {
            var threshold = DateTime.UtcNow - _options.RetentionPeriod;
            var oldOutbox = _dbContext.OutboxMessages.Where(x => (x.Status == "Sent" || x.Status == "Failed") && x.CreatedAt < threshold).ToList();
            var oldInbox = _dbContext.InboxMessages.Where(x => (x.Status == "Processed" || x.Status == "Failed") && x.ReceivedAt < threshold).ToList();
            if (oldOutbox.Any() || oldInbox.Any())
            {
                _dbContext.OutboxMessages.RemoveRange(oldOutbox);
                _dbContext.InboxMessages.RemoveRange(oldInbox);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Cleanup removed {Outbox} outbox and {Inbox} inbox messages", oldOutbox.Count, oldInbox.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cleanup worker failure");
        }
    }
}
