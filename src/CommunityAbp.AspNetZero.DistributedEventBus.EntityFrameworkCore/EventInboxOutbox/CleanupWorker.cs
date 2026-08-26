using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Abp.Dependency;
using Abp.Threading.BackgroundWorkers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

public interface ICleanupWorker : IBackgroundWorker { }

public sealed class CleanupWorker : ICleanupWorker, ISingletonDependency, IDisposable
{
    private readonly IIocResolver _resolver;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private readonly ILogger<CleanupWorker> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _timer;

    public CleanupWorker(IIocResolver resolver, AspNetZeroEventBusBoxesOptions options, ILogger<CleanupWorker> logger)
    {
        _resolver = resolver;
        _options = options;
        _logger = logger;
    }

    public void Start()
    {
        _timer ??= new Timer(static state => ((CleanupWorker)state!).ScheduleCleanup(), this, TimeSpan.Zero, _options.CleanOldEventTimeIntervalSpan);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void WaitToStop() { }

    private void ScheduleCleanup()
    {
        _ = CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        if (!await _gate.WaitAsync(0))
        {
            return;
        }

        try
        {
            using var scope = _resolver.CreateScope();
            var dbContext = scope.Resolve<DistributedEventBusDbContext>();
            var threshold = DateTime.UtcNow - _options.RetentionPeriod;

            var outboxDeleted = await dbContext.OutboxMessages
                .Where(x => (x.Status == "Sent" || x.Status == "Failed") && x.CreatedAt < threshold)
                .ExecuteDeleteAsync();
            var inboxDeleted = await dbContext.InboxMessages
                .Where(x => (x.Status == "Processed" || x.Status == "Failed") && x.ReceivedAt < threshold)
                .ExecuteDeleteAsync();

            if (outboxDeleted != 0 || inboxDeleted != 0)
            {
                _logger.LogInformation("Cleanup removed {Outbox} outbox and {Inbox} inbox messages", outboxDeleted, inboxDeleted);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Cleanup worker failure");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _gate.Dispose();
    }
}
