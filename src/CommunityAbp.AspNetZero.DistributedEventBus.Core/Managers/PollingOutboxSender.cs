using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using Microsoft.Extensions.Logging;
using Abp.Dependency;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

public class PollingOutboxSender : IOutboxSender, ISingletonDependency
{
    private readonly ILogger<PollingOutboxSender> _logger;
    private readonly IDistributedEventBus _bus;
    private readonly IEventOutbox _outbox;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private CancellationTokenSource? _cts;
    private OutboxConfig? _config;

    public PollingOutboxSender(ILogger<PollingOutboxSender> logger, IDistributedEventBus bus, IEventOutbox outbox, AspNetZeroEventBusBoxesOptions options)
    {
        _logger = logger;
        _bus = bus;
        _outbox = outbox;
        _options = options;
    }

    public Task StartAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken = default)
    {
        _config = outboxConfig;
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
                var pending = await _outbox.GetPendingAsync(_options.OutboxBatchSize, ct);
                foreach (var evt in pending)
                {
                    var type = Type.GetType(evt.EventName);
                    if (type == null)
                    {
                        await _outbox.MarkFailedAsync(evt.Id, "Type not found", ct);
                        continue;
                    }
                    try
                    {
                        var obj = System.Text.Json.JsonSerializer.Deserialize(evt.EventData, type);
                        if (obj != null)
                        {
                            await _bus.PublishAsync(type, obj, onUnitOfWorkComplete: false, useOutbox: false);
                            await _outbox.MarkSentAsync(evt.Id, ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send outbox event {EventId}");
                        await _outbox.MarkFailedAsync(evt.Id, ex.Message, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox polling failure");
            }

            await Task.Delay(_options.OutboxPollingInterval, ct); // configurable interval
        }
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }
}
