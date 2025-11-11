using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using System.Threading;
using Abp.Dependency;
using System.Collections.Immutable;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

#if NETSTANDARD2_0
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISupportsEventBoxes, IDisposable
#else
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISupportsEventBoxes, IDisposable, IAsyncDisposable
#endif
{
    private ImmutableDictionary<Type, ImmutableList<Func<object, Task>>> _handlers = ImmutableDictionary<Type, ImmutableList<Func<object, Task>>>.Empty;
    private readonly object _handlersLock = new();
    private bool _disposed;
    private readonly DistributedEventBusOptions _options; // injected singleton
    private readonly Dictionary<string, IEventOutbox> _outboxCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IIocManager IocManager;
    private readonly IEventSerializer _serializer;

    public DistributedEventBusBase(DistributedEventBusOptions options, IIocManager iocManager, IEventSerializer serializer)
    {
        _options = options;
        IocManager = iocManager;
        _serializer = serializer;
    }

    // No-op: retained for interface compatibility
    public void InitializeSubscriptions() { }

    public virtual Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true) where TEvent : class
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        return PublishAsync(typeof(TEvent), eventData, onUnitOfWorkComplete, useOutbox);
    }

    public virtual async Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    {
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));

        if (!useOutbox)
        {
            await DispatchAsync(eventType, eventData);
            return;
        }

        if (_options.Outboxes.Count == 0)
        {
            throw new InvalidOperationException("No outboxes configured while useOutbox=true.");
        }

        var matchingOutboxes = _options.Outboxes
        .Where(kv => (kv.Value.Selector == null || kv.Value.Selector(eventType)))
        .ToList();

        if (matchingOutboxes.Count == 0)
        {
            await DispatchAsync(eventType, eventData);
            return;
        }

        var bytes = _serializer.Serialize(eventData, eventType);
        var typeIdentifier = _serializer.GetTypeIdentifier(eventType);

        foreach (var kv in matchingOutboxes)
        {
            var name = kv.Key;
            var outboxConfig = kv.Value;
            var outbox = ResolveOutbox(name, outboxConfig);
            if (outbox == null) continue;
            var outgoing = new OutgoingEventInfo(Guid.NewGuid(), typeIdentifier, bytes, DateTime.UtcNow);
            await outbox.AddAsync(outgoing, CancellationToken.None);
        }
    }

    private IEventOutbox? ResolveOutbox(string name, OutboxConfig config)
    {
        if (_outboxCache.TryGetValue(name, out var cached)) return cached;
        if (config.Factory != null)
        {
            try
            {
                var created = config.Factory(IocManager, config);
                if (created != null) { _outboxCache[name] = created; return created; }
            }
            catch { }
        }
        if (config.ImplementationType != null && typeof(IEventOutbox).IsAssignableFrom(config.ImplementationType))
        {
            if (IocManager.IsRegistered(config.ImplementationType))
            {
                try
                {
                    var resolvedExisting = (IEventOutbox)IocManager.Resolve(config.ImplementationType);
                    _outboxCache[name] = resolvedExisting;
                    return resolvedExisting;
                }
                catch { }
            }
        }
        try
        {
            if (IocManager.IsRegistered<IEventOutbox>())
            {
                var singleton = IocManager.Resolve<IEventOutbox>();
                _outboxCache[name] = singleton;
                return singleton;
            }
        }
        catch { }
        return null;
    }

    private Task DispatchAsync(Type eventType, object eventData)
    {
        var collected = new List<Func<object, Task>>();
        var current = eventType;
        while (current != null && current != typeof(object))
        {
            if (_handlers.TryGetValue(current, out var list) && list.Count > 0) collected.AddRange(list);
            current = current.BaseType;
        }
        if (collected.Count == 0) return Task.CompletedTask;
        var distinct = collected.Distinct().ToList();
        if (distinct.Count == 1) return distinct[0](eventData);
        return Task.WhenAll(distinct.Select(h => h(eventData)));
    }

    public virtual IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler) where TEvent : class
    {
        if (handler == null) throw new ArgumentNullException(nameof(handler));
        var eventType = typeof(TEvent);
        async Task Wrapper(object e) => await handler.HandleEventAsync((TEvent)e);
        lock (_handlersLock)
        {
            var current = _handlers.TryGetValue(eventType, out var list) ? list : ImmutableList<Func<object, Task>>.Empty;
            _handlers = _handlers.SetItem(eventType, current.Add(Wrapper));
        }
        return new ActionDisposer(() =>
        {
            lock (_handlersLock)
            {
                if (_handlers.TryGetValue(eventType, out var list))
                {
                    var updated = list.Remove(Wrapper);
                    _handlers = updated.Count == 0 ? _handlers.Remove(eventType) : _handlers.SetItem(eventType, updated);
                }
            }
        });
    }

    public async Task PublishFromOutboxAsync(OutgoingEventInfo outgoingEvent, OutboxConfig outboxConfig)
    {
        if (outgoingEvent == null) throw new ArgumentNullException(nameof(outgoingEvent));
        var type = _serializer.ResolveType(outgoingEvent.EventName);
        if (type == null) return;
        try
        {
            var obj = _serializer.Deserialize(outgoingEvent.EventData, type);
            if (obj != null) await DispatchAsync(type, obj);
        }
        catch { }
    }

    public async Task PublishManyFromOutboxAsync(IEnumerable<OutgoingEventInfo> outgoingEvents, OutboxConfig outboxConfig)
    {
        foreach (var evt in outgoingEvents) await PublishFromOutboxAsync(evt, outboxConfig);
    }

    public async Task ProcessFromInboxAsync(IncomingEventInfo incomingEvent, InboxConfig inboxConfig)
    {
        if (incomingEvent == null) throw new ArgumentNullException(nameof(incomingEvent));
        var type = _serializer.ResolveType(incomingEvent.EventName);
        if (type == null) return;
        try
        {
            var obj = _serializer.Deserialize(incomingEvent.EventData, type);
            if (obj != null) await DispatchAsync(type, obj);
        }
        catch { }
    }

    private sealed class ActionDisposer : IDisposable
    {
        private readonly Action _dispose; public ActionDisposer(Action dispose) => _dispose = dispose; public void Dispose() => _dispose();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _handlers = ImmutableDictionary<Type, ImmutableList<Func<object, Task>>>.Empty;
                _outboxCache.Clear();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken, bool onUnitOfWorkComplete = true, bool useOutbox = true) where TEvent : class
    => PublishAsync(eventData!, onUnitOfWorkComplete, useOutbox);

    public Task PublishAsync(Type eventType, object eventData, CancellationToken cancellationToken, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    => PublishAsync(eventType, eventData, onUnitOfWorkComplete, useOutbox);

    public IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler, CancellationToken cancellationToken) where TEvent : class
    => Subscribe(handler);

#if !NETSTANDARD2_0
 public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
#endif
}
