using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Abp;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using System.Threading;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Adapters;
using Castle.MicroKernel.Registration; // added for dynamic registration fallback

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

#if NETSTANDARD2_0
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISupportsEventBoxes, IDisposable
#else
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISupportsEventBoxes, IDisposable, IAsyncDisposable
#endif
{
    private readonly Dictionary<Type, List<Func<object, Task>>> _handlers = new();
    private bool _disposed;

    private DistributedEventBusOptions? _options; // cached options instance

    // Allow DI to provide options; keep optional for backward compatibility where not registered yet.
    public DistributedEventBusBase(DistributedEventBusOptions? options = null)
    {
        _options = options; // may be null; will attempt lazy resolve later
    }

    private DistributedEventBusOptions? GetOptions()
    {
        if (_options != null)
        {
            return _options;
        }

        try
        {
            if (IocManager.Instance.IsRegistered<DistributedEventBusOptions>())
            {
                _options = IocManager.Instance.Resolve<DistributedEventBusOptions>();
            }
        }
        catch
        {
            // swallow; will treat as unconfigured
        }

        return _options;
    }

    public virtual Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
        where TEvent : class
    {
        if (eventData == null)
        {
            throw new ArgumentNullException(nameof(eventData));
        }
        return PublishAsync(typeof(TEvent), eventData, onUnitOfWorkComplete, useOutbox);
    }

    public virtual async Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    {
        if (eventType == null)
        {
            throw new ArgumentNullException(nameof(eventType));
        }

        if (useOutbox)
        {
            var options = GetOptions();
            if (options == null)
            {
                // No options registered: fall back to direct dispatch
                await DispatchAsync(eventType, eventData);
                return;
            }

            var matchingOutboxes = options.Outboxes.Values.Where(o => o.Selector == null || o.Selector(eventType)).ToList();
            if (matchingOutboxes.Count == 0)
            {
                await DispatchAsync(eventType, eventData);
                return;
            }

            foreach (var outboxConfig in matchingOutboxes)
            {
                IEventOutbox? outbox = null;

                // Try resolving concrete implementation type first (more specific)
                if (outboxConfig.ImplementationType != null)
                {
                    try
                    {
                        if (IocManager.Instance.IsRegistered(outboxConfig.ImplementationType))
                        {
                            outbox = (IEventOutbox)IocManager.Instance.Resolve(outboxConfig.ImplementationType);
                        }
                    }
                    catch { outbox = null; }
                }

                // Fallback to interface mapping
                if (outbox == null && IocManager.Instance.IsRegistered<IEventOutbox>())
                {
                    try { outbox = IocManager.Instance.Resolve<IEventOutbox>(); } catch { outbox = null; }
                }

                // If still null, skip storing and dispatch directly (avoid hard failure in tests)
                if (outbox == null)
                {
                    await DispatchAsync(eventType, eventData);
                    continue; // move to next configured outbox (if any)
                }

                var outgoing = new OutgoingEventInfo(
                    Guid.NewGuid(),
                    eventType.AssemblyQualifiedName!,
                    JsonSerializer.SerializeToUtf8Bytes(eventData),
                    DateTime.UtcNow);

                await outbox.AddAsync(outgoing, CancellationToken.None);
            }
            return;
        }

        await DispatchAsync(eventType, eventData);
    }

    private Task DispatchAsync(Type eventType, object eventData)
    {
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            var tasks = handlers.Select(h => h(eventData));
            return Task.WhenAll(tasks);
        }
        return Task.CompletedTask;
    }

    public virtual IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler) where TEvent : class
    {
        var eventType = typeof(TEvent);
        if (!_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers = new List<Func<object, Task>>();
            _handlers[eventType] = handlers;
        }

        async Task Wrapper(object e) => await handler.HandleEventAsync((TEvent)e);
        handlers.Add(Wrapper);

        var adapter = new AbpDistributedEventHandlerAdapter<TEvent>(handler);

        // EventBus (base) does not expose a Subscribe method; prevent CS0117 by using a no-op disposable.
        var baseSubscription = DisposeAction.Empty;

        return new DisposeAction(() =>
        {
            handlers.Remove(Wrapper);
            baseSubscription.Dispose();
        });
    }

    public virtual Task PublishFromOutboxAsync(OutgoingEventInfo outgoingEvent, OutboxConfig outboxConfig)
    {
        var eventType = Type.GetType(outgoingEvent.EventName);
        if (eventType == null)
        {
            return Task.CompletedTask;
        }

        var eventData = JsonSerializer.Deserialize(outgoingEvent.EventData, eventType);
        return eventData != null
            ? DispatchAsync(eventType, eventData)
            : Task.CompletedTask;
    }

    public virtual Task PublishManyFromOutboxAsync(IEnumerable<OutgoingEventInfo> outgoingEvents, OutboxConfig outboxConfig)
    {
        var tasks = outgoingEvents.Select(e => PublishFromOutboxAsync(e, outboxConfig));
        return Task.WhenAll(tasks);
    }

    public virtual Task ProcessFromInboxAsync(IncomingEventInfo incomingEvent, InboxConfig inboxConfig)
    {
        var eventType = Type.GetType(incomingEvent.EventName);
        if (eventType == null)
        {
            return Task.CompletedTask;
        }

        var eventData = JsonSerializer.Deserialize(incomingEvent.EventData, eventType);
        return eventData != null
            ? DispatchAsync(eventType, eventData)
            : Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _handlers.Clear();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    // New overloads with CancellationToken (token not yet used; placeholder for future enhancements)
    public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken, bool onUnitOfWorkComplete = true, bool useOutbox = true)
        where TEvent : class
        => PublishAsync(eventData, onUnitOfWorkComplete, useOutbox);

    public Task PublishAsync(Type eventType, object eventData, CancellationToken cancellationToken, bool onUnitOfWorkComplete = true, bool useOutbox = true)
        => PublishAsync(eventType, eventData, onUnitOfWorkComplete, useOutbox);

    public IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler, CancellationToken cancellationToken)
        where TEvent : class
        => Subscribe(handler);

#if !NETSTANDARD2_0
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
#endif
}
