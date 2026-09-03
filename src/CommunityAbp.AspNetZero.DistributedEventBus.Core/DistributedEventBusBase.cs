using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Abp.Dependency;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

#if NETSTANDARD2_0
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ILocalDistributedEventDispatcher, ISupportsEventBoxes, IDisposable
#else
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ILocalDistributedEventDispatcher, ISupportsEventBoxes, IDisposable, IAsyncDisposable
#endif
{
    private ImmutableDictionary<Type, ImmutableList<Func<object, Task>>> _handlers = ImmutableDictionary<Type, ImmutableList<Func<object, Task>>>.Empty;
    private readonly List<SubscriptionEntry> _subscriptions = [];
    private readonly object _handlersLock = new();
    private readonly Dictionary<string, IEventOutbox> _outboxCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DistributedEventBusOptions _options;
    private readonly IIocManager _iocManager;
    private readonly IEventSerializer _serializer;
    private readonly IEventTypeRegistry _eventTypes;
    private readonly IDistributedEventContextAccessor _contextAccessor;
    private bool _disposed;

    public DistributedEventBusBase(DistributedEventBusOptions options, IIocManager iocManager, IEventSerializer serializer,
        IEventTypeRegistry? eventTypes = null, IDistributedEventContextAccessor? contextAccessor = null)
    {
        _options = options;
        _iocManager = iocManager;
        _serializer = serializer;
        _eventTypes = eventTypes ?? new EventTypeRegistry();
        _contextAccessor = contextAccessor ?? new DistributedEventContextAccessor();
    }

    public void InitializeSubscriptions() { }

    public virtual Task PublishAsync<TEvent>(TEvent eventData, DistributedEventDispatchMode dispatchMode,
        bool onUnitOfWorkComplete = true, CancellationToken cancellationToken = default) where TEvent : class =>
        PublishAsync(typeof(TEvent), eventData ?? throw new ArgumentNullException(nameof(eventData)), dispatchMode, onUnitOfWorkComplete, cancellationToken);

    public virtual async Task PublishAsync(Type eventType, object eventData, DistributedEventDispatchMode dispatchMode,
        bool onUnitOfWorkComplete = true, CancellationToken cancellationToken = default)
    {
        if (eventType is null) throw new ArgumentNullException(nameof(eventType));
        if (eventData is null) throw new ArgumentNullException(nameof(eventData));
        cancellationToken.ThrowIfCancellationRequested();
        _eventTypes.Register(eventType);
        var eventName = _eventTypes.GetEventName(eventType);
        using var activity = DistributedEventBusDiagnostics.ActivitySource.StartActivity("distributed-eventbus.publish", ActivityKind.Producer);
        activity?.SetTag("messaging.operation", "publish");
        activity?.SetTag("messaging.message.type", eventName);
        activity?.SetTag("distributed_eventbus.dispatch_mode", dispatchMode.ToString());

        if (dispatchMode == DistributedEventDispatchMode.Direct)
        {
            var dispatchLocally = DispatchLocallyOnDirectPublish;
            activity?.SetTag("distributed_eventbus.local_dispatch", dispatchLocally);
            if (dispatchLocally) await DispatchAsync(eventType, eventData, NewContext(eventType, eventName, dispatchMode), cancellationToken);
            return;
        }
        if (_options.Outboxes.Count == 0) throw new InvalidOperationException("No outboxes configured while dispatch mode is Outbox.");
        var matchingOutboxes = _options.Outboxes.Where(x => x.Value.Selector is null || x.Value.Selector(eventType)).ToList();
        if (matchingOutboxes.Count == 0)
        {
            await DispatchAsync(eventType, eventData, NewContext(eventType, eventName, dispatchMode), cancellationToken);
            return;
        }

        var bytes = _serializer.Serialize(eventData, eventType);
        foreach (var item in matchingOutboxes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outbox = ResolveOutbox(item.Key, item.Value);
            if (outbox is null) continue;
            using var persistActivity = DistributedEventBusDiagnostics.ActivitySource.StartActivity("distributed-eventbus.outbox.persist", ActivityKind.Producer);
            persistActivity?.SetTag("messaging.message.type", eventName);
            await outbox.AddAsync(new OutgoingEventInfo(Guid.NewGuid(), eventName, bytes, DateTime.UtcNow), cancellationToken);
        }
    }

    [Obsolete("Use the overload accepting DistributedEventDispatchMode.")]
    public virtual Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = false) where TEvent : class =>
        PublishAsync(eventData, useOutbox ? DistributedEventDispatchMode.Outbox : DistributedEventDispatchMode.Direct, onUnitOfWorkComplete);

    [Obsolete("Use the overload accepting DistributedEventDispatchMode.")]
    public virtual Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = false) =>
        PublishAsync(eventType, eventData, useOutbox ? DistributedEventDispatchMode.Outbox : DistributedEventDispatchMode.Direct, onUnitOfWorkComplete);

    [Obsolete("Use the overload accepting DistributedEventDispatchMode.")]
    public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken, bool onUnitOfWorkComplete = true, bool useOutbox = false) where TEvent : class =>
        PublishAsync(eventData, useOutbox ? DistributedEventDispatchMode.Outbox : DistributedEventDispatchMode.Direct, onUnitOfWorkComplete, cancellationToken);

    [Obsolete("Use the overload accepting DistributedEventDispatchMode.")]
    public Task PublishAsync(Type eventType, object eventData, CancellationToken cancellationToken, bool onUnitOfWorkComplete = true, bool useOutbox = false) =>
        PublishAsync(eventType, eventData, useOutbox ? DistributedEventDispatchMode.Outbox : DistributedEventDispatchMode.Direct, onUnitOfWorkComplete, cancellationToken);

    public Task DispatchLocalAsync(Type eventType, object eventData, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _eventTypes.Register(eventType);
        return DispatchAsync(eventType, eventData, _contextAccessor.Current ?? NewContext(eventType, GetEventName(eventType), DistributedEventDispatchMode.Direct), cancellationToken);
    }

    public virtual IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler) where TEvent : class
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var eventType = typeof(TEvent);
        lock (_handlersLock)
        {
            var existing = _subscriptions.FirstOrDefault(x => x.EventType == eventType && ReferenceEquals(x.Handler, handler));
            if (existing is not null) return existing.Subscription;
            async Task Wrapper(object e) => await handler.HandleEventAsync((TEvent)e);
            var subscription = new ActionDisposer(() => RemoveSubscription(eventType, handler, Wrapper));
            _subscriptions.Add(new SubscriptionEntry(eventType, handler, subscription));
            var current = _handlers.TryGetValue(eventType, out var list) ? list : ImmutableList<Func<object, Task>>.Empty;
            _handlers = _handlers.SetItem(eventType, current.Add(Wrapper));
            return subscription;
        }
    }

    public IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler, CancellationToken cancellationToken) where TEvent : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Subscribe(handler);
    }

    public async Task PublishFromOutboxAsync(OutgoingEventInfo outgoingEvent, OutboxConfig outboxConfig)
    {
        var type = ResolveEventType(outgoingEvent.EventName);
        var eventData = type is null ? null : _serializer.Deserialize(outgoingEvent.EventData, type);
        if (type is not null && eventData is not null) await DispatchLocalAsync(type, eventData);
    }

    public async Task PublishManyFromOutboxAsync(IEnumerable<OutgoingEventInfo> outgoingEvents, OutboxConfig outboxConfig)
    {
        foreach (var outgoingEvent in outgoingEvents) await PublishFromOutboxAsync(outgoingEvent, outboxConfig);
    }

    public async Task ProcessFromInboxAsync(IncomingEventInfo incomingEvent, InboxConfig inboxConfig)
    {
        var type = ResolveEventType(incomingEvent.EventName);
        var eventData = type is null ? null : _serializer.Deserialize(incomingEvent.EventData, type);
        if (type is null || eventData is null) return;

        var context = incomingEvent.MessageContext ?? new DistributedEventMessageContext
        {
            MessageId = incomingEvent.MessageId,
            EventName = incomingEvent.EventName,
            CorrelationId = incomingEvent.GetCorrelationId(),
            DispatchMode = DistributedEventDispatchMode.Direct,
            CreatedAtUtc = incomingEvent.CreationTime
        };
        using var contextScope = _contextAccessor.Push(context);
        await DispatchLocalAsync(type, eventData);
    }

    /// <summary>
    ///     Controls whether a <see cref="DistributedEventDispatchMode.Direct"/> publish invokes the handlers registered on
    ///     this instance. The transport-less base bus returns <c>true</c>. A transport that delivers a copy of every
    ///     published message back to the same process (for example a Service Bus topic subscription or queue that this
    ///     instance also consumes) overrides this to <c>false</c> so handlers run exactly once, from the broker copy,
    ///     with real broker metadata in <see cref="IDistributedEventContextAccessor.Current"/>.
    /// </summary>
    protected virtual bool DispatchLocallyOnDirectPublish => true;

    /// <summary>True once <see cref="Dispose()"/> has run.</summary>
    protected bool IsDisposed => _disposed;

    protected Type? ResolveEventType(string identifier) => _eventTypes.Resolve(identifier) ?? _serializer.ResolveType(identifier);
    protected string GetEventName(Type eventType) { _eventTypes.Register(eventType); return _eventTypes.GetEventName(eventType); }
    protected IDistributedEventContextAccessor ContextAccessor => _contextAccessor;

    private DistributedEventMessageContext NewContext(Type type, string eventName, DistributedEventDispatchMode mode) => new()
    {
        MessageId = Guid.NewGuid().ToString("N"), EventName = eventName, LegacyTypeIdentifier = _serializer.GetTypeIdentifier(type), DispatchMode = mode
    };

    private IEventOutbox? ResolveOutbox(string name, OutboxConfig config)
    {
        if (_outboxCache.TryGetValue(name, out var cached)) return cached;
        if (config.Factory is not null) return _outboxCache[name] = config.Factory(_iocManager, config);
        if (config.ImplementationType is not null && typeof(IEventOutbox).IsAssignableFrom(config.ImplementationType) && _iocManager.IsRegistered(config.ImplementationType))
            return _outboxCache[name] = (IEventOutbox)_iocManager.Resolve(config.ImplementationType);
        return _iocManager.IsRegistered<IEventOutbox>() ? _outboxCache[name] = _iocManager.Resolve<IEventOutbox>() : null;
    }

    private async Task DispatchAsync(Type eventType, object eventData, DistributedEventMessageContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handlers = new HashSet<Func<object, Task>>();
        for (var current = eventType; current is not null && current != typeof(object); current = current.BaseType)
            if (_handlers.TryGetValue(current, out var list)) foreach (var handler in list) handlers.Add(handler);
        if (handlers.Count == 0) return;
        using var contextScope = _contextAccessor.Push(context);
        using var activity = DistributedEventBusDiagnostics.ActivitySource.StartActivity("distributed-eventbus.dispatch", ActivityKind.Consumer);
        activity?.SetTag("messaging.message.id", context.MessageId);
        activity?.SetTag("messaging.message.type", context.EventName);
        activity?.SetTag("messaging.delivery.count", context.DeliveryCount);
        await Task.WhenAll(handlers.Select(async handler =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var handlerActivity = DistributedEventBusDiagnostics.ActivitySource.StartActivity("distributed-eventbus.handler", ActivityKind.Internal);
            await handler(eventData);
        }));
    }

    private void RemoveSubscription(Type eventType, object handler, Func<object, Task> wrapper)
    {
        lock (_handlersLock)
        {
            _subscriptions.RemoveAll(x => x.EventType == eventType && ReferenceEquals(x.Handler, handler));
            if (_handlers.TryGetValue(eventType, out var list))
            {
                var updated = list.Remove(wrapper);
                _handlers = updated.Count == 0 ? _handlers.Remove(eventType) : _handlers.SetItem(eventType, updated);
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing) { _handlers = ImmutableDictionary<Type, ImmutableList<Func<object, Task>>>.Empty; _subscriptions.Clear(); _outboxCache.Clear(); }
        _disposed = true;
    }
    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
#if !NETSTANDARD2_0
    public virtual ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
#endif

    private sealed class SubscriptionEntry(Type eventType, object handler, IDisposable subscription)
    { public Type EventType { get; } = eventType; public object Handler { get; } = handler; public IDisposable Subscription { get; } = subscription; }
    private sealed class ActionDisposer(Action dispose) : IDisposable
    { private Action? _dispose = dispose; public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
}
