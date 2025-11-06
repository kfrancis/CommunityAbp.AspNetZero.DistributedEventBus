using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Abp.Dependency;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

public class PollingInboxProcessor : IInboxProcessor, ISingletonDependency
{
    private readonly ILogger<PollingInboxProcessor> _logger;
    private readonly IDistributedEventBus _bus;
    private readonly IEventInbox _inbox;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private CancellationTokenSource? _cts;

    public PollingInboxProcessor(ILogger<PollingInboxProcessor> logger, IDistributedEventBus bus, IEventInbox inbox, AspNetZeroEventBusBoxesOptions options)
    {
        _logger = logger;
        _bus = bus;
        _inbox = inbox;
        _options = options;
    }

    public Task StartAsync(InboxConfig inboxConfig, CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pending = await _inbox.GetPendingAsync(_options.InboxBatchSize, ct);
                foreach (var evt in pending)
                {
                    var type = Type.GetType(evt.EventName);
                    if (type == null)
                    {
                        await _inbox.MarkFailedAsync(evt.Id, "Type not found", ct);
                        continue;
                    }
                    try
                    {
                        var obj = System.Text.Json.JsonSerializer.Deserialize(evt.EventData, type);
                        if (obj != null)
                        {
                            await _bus.PublishAsync(type, obj, onUnitOfWorkComplete: false, useOutbox: false);
                            await _inbox.MarkProcessedAsync(evt.Id, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed processing inbox event {EventId}");
                        await _inbox.MarkFailedAsync(evt.Id, ex.Message, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inbox polling failure");
            }

            await Task.Delay(_options.InboxPollingInterval, ct);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }
}
