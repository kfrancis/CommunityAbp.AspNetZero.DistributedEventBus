using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using Microsoft.Extensions.Logging;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

public sealed class PollingOutboxSender : IOutboxSender, ITransientDependency
{
    private readonly ILogger<PollingOutboxSender> _logger;
    private readonly IDistributedEventBus _bus;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private readonly IIocResolver _resolver;
    private readonly IEventSerializer _serializer;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PollingOutboxSender(ILogger<PollingOutboxSender> logger, IDistributedEventBus bus, AspNetZeroEventBusBoxesOptions options, IIocResolver resolver, IEventSerializer serializer)
    {
        _logger = logger;
        _bus = bus;
        _options = options;
        _resolver = resolver;
        _serializer = serializer;
    }

    public Task StartAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken = default)
    {
        if (outboxConfig is null) throw new ArgumentNullException(nameof(outboxConfig));
        if (_loop is not null) throw new InvalidOperationException("This outbox sender has already been started.");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(outboxConfig, _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessBatchAsync(outboxConfig, cancellationToken);
                await Task.Delay(_options.OutboxPollingInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessBatchAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken)
    {
        IReadOnlyList<OutgoingEventInfo> pending;
        try
        {
            using var scope = _resolver.CreateScope();
            var outbox = ResolveOutbox(outboxConfig, scope);
            if (outbox is null)
            {
                _logger.LogWarning("PollingOutboxSender could not resolve outbox for {Type}.", outboxConfig.ImplementationType?.FullName ?? "(null)");
                return;
            }
            await outbox.RequeueExpiredClaimsAsync(_options.ProcessingLeaseTimeout, cancellationToken);
            pending = (await outbox.GetPendingAsync(_options.OutboxBatchSize, cancellationToken)).ToList();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Outbox polling failure");
            return;
        }

        await ProcessConcurrentlyAsync(pending, outgoingEvent => ProcessEventAsync(outboxConfig, outgoingEvent, cancellationToken), cancellationToken);
    }

    private async Task ProcessEventAsync(OutboxConfig outboxConfig, OutgoingEventInfo outgoingEvent, CancellationToken cancellationToken)
    {
        using var scope = _resolver.CreateScope();
        var outbox = ResolveOutbox(outboxConfig, scope);
        if (outbox is null || !await outbox.TryClaimAsync(outgoingEvent.Id, cancellationToken)) return;

        try
        {
            var eventType = _serializer.ResolveType(outgoingEvent.EventName);
            if (eventType is null)
            {
                await outbox.MarkFailedAsync(outgoingEvent.Id, "Type not found", cancellationToken);
                return;
            }

            var eventData = _serializer.Deserialize(outgoingEvent.EventData, eventType);
            if (eventData is null)
            {
                await outbox.MarkFailedAsync(outgoingEvent.Id, "Deserialization returned null", cancellationToken);
                return;
            }

            await _bus.PublishAsync(eventType, eventData, cancellationToken, onUnitOfWorkComplete: false, useOutbox: false);
            await outbox.MarkSentAsync(outgoingEvent.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send outbox event {EventId}", outgoingEvent.Id);
            try { await outbox.MarkFailedAsync(outgoingEvent.Id, exception.Message, cancellationToken); } catch { }
        }
    }

    private static IEventOutbox? ResolveOutbox(OutboxConfig config, IIocResolver resolver)
    {
        if (config.Factory is not null) return config.Factory(resolver, config);
        if (config.ImplementationType is not null && typeof(IEventOutbox).IsAssignableFrom(config.ImplementationType) && resolver.IsRegistered(config.ImplementationType)) return (IEventOutbox)resolver.Resolve(config.ImplementationType);
        return resolver.IsRegistered<IEventOutbox>() ? resolver.Resolve<IEventOutbox>() : null;
    }

    private async Task ProcessConcurrentlyAsync(IEnumerable<OutgoingEventInfo> events, Func<OutgoingEventInfo, Task> processEvent, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(_options.MaxConcurrentEventHandlers, _options.MaxConcurrentEventHandlers);
        var tasks = events.Select(async outgoingEvent =>
        {
            await gate.WaitAsync(cancellationToken);
            try { await processEvent(outgoingEvent); }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts is null) return;
        _cts.Cancel();
        if (_loop is not null) await _loop;
        _cts.Dispose();
        _cts = null;
        _loop = null;
    }
}
