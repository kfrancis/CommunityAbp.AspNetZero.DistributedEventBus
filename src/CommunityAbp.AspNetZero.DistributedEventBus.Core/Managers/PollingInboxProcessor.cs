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

public sealed class PollingInboxProcessor : IInboxProcessor, ITransientDependency
{
    private readonly ILogger<PollingInboxProcessor> _logger;
    private readonly IDistributedEventBus _bus;
    private readonly IIocResolver _resolver;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PollingInboxProcessor(ILogger<PollingInboxProcessor> logger, IDistributedEventBus bus, IIocResolver resolver, AspNetZeroEventBusBoxesOptions options)
    {
        _logger = logger;
        _bus = bus;
        _resolver = resolver;
        _options = options;
    }

    public Task StartAsync(InboxConfig inboxConfig, CancellationToken cancellationToken = default)
    {
        if (inboxConfig is null) throw new ArgumentNullException(nameof(inboxConfig));
        if (_loop is not null) throw new InvalidOperationException("This inbox processor has already been started.");
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(inboxConfig, _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(InboxConfig inboxConfig, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await ProcessBatchAsync(inboxConfig, cancellationToken);
                await Task.Delay(_options.InboxPollingInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ProcessBatchAsync(InboxConfig inboxConfig, CancellationToken cancellationToken)
    {
        IReadOnlyList<IncomingEventInfo> pending;
        try
        {
            using var scope = _resolver.CreateScope();
            var inbox = ResolveInbox(inboxConfig, scope);
            if (inbox is null)
            {
                _logger.LogWarning("PollingInboxProcessor could not resolve inbox for {Type}.", inboxConfig.ImplementationType?.FullName ?? "(null)");
                return;
            }
            await inbox.RequeueExpiredClaimsAsync(_options.ProcessingLeaseTimeout, cancellationToken);
            pending = await inbox.GetPendingAsync(_options.InboxBatchSize, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Inbox polling failure");
            return;
        }

        await ProcessConcurrentlyAsync(pending, incomingEvent => ProcessEventAsync(inboxConfig, incomingEvent, cancellationToken), cancellationToken);
    }

    private async Task ProcessEventAsync(InboxConfig inboxConfig, IncomingEventInfo incomingEvent, CancellationToken cancellationToken)
    {
        using var scope = _resolver.CreateScope();
        var inbox = ResolveInbox(inboxConfig, scope);
        if (inbox is null || !await inbox.TryClaimAsync(incomingEvent.Id, cancellationToken)) return;

        try
        {
            if (_bus is not ISupportsEventBoxes eventBoxes) throw new InvalidOperationException("The configured event bus does not support inbox dispatch.");
            await eventBoxes.ProcessFromInboxAsync(incomingEvent, inboxConfig);
            await inbox.MarkProcessedAsync(incomingEvent.Id, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed processing inbox event {EventId}", incomingEvent.Id);
            try { await inbox.MarkFailedAsync(incomingEvent.Id, exception.Message, cancellationToken); } catch { }
        }
    }

    private static IEventInbox? ResolveInbox(InboxConfig config, IIocResolver resolver)
    {
        if (config.ImplementationType is not null && typeof(IEventInbox).IsAssignableFrom(config.ImplementationType) && resolver.IsRegistered(config.ImplementationType)) return (IEventInbox)resolver.Resolve(config.ImplementationType);
        return resolver.IsRegistered<IEventInbox>() ? resolver.Resolve<IEventInbox>() : null;
    }

    private async Task ProcessConcurrentlyAsync(IEnumerable<IncomingEventInfo> events, Func<IncomingEventInfo, Task> processEvent, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(_options.MaxConcurrentEventHandlers, _options.MaxConcurrentEventHandlers);
        var tasks = events.Select(async incomingEvent =>
        {
            await gate.WaitAsync(cancellationToken);
            try { await processEvent(incomingEvent); }
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
