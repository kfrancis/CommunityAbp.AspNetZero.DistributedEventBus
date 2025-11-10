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
using System.Reflection;

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
    private readonly List<IDisposable> _autoSubscriptions = new(); // track auto subscriptions for disposal
    private readonly HashSet<Type> _pendingHandlers = new(); // handler types that failed to resolve earlier
    private bool _initialSubscriptionsAttempted;

    // Marker types we treat as optional (skip auto-subscription if not resolvable)
    private static readonly string[] OptionalHandlerTypeNames = new[] {"PatientManagement.Web.Common.BackgroundJobEventHub"};

    private bool IsOptionalHandler(Type t) => OptionalHandlerTypeNames.Contains(t.FullName);

    public DistributedEventBusBase(DistributedEventBusOptions options, IIocManager iocManager, IEventSerializer serializer)
    {
        _options = options;
        IocManager = iocManager;
        _serializer = serializer;
    }

    // Deferred initialization entry point
    public void InitializeSubscriptions() => TryAutoSubscribeRegisteredHandlers();

    private void TryAutoSubscribeRegisteredHandlers()
    {
        _initialSubscriptionsAttempted = true;
        if (_options.Handlers.Count == 0)
        {
            return; // nothing declared
        }

        foreach (var handlerType in _options.Handlers)
        {
            try
            {
                if (!IocManager.IsRegistered(handlerType))
                {
                    if (!IsOptionalHandler(handlerType)) _pendingHandlers.Add(handlerType);
                    continue;
                }

                object handlerInstance;
                try
                {
                    handlerInstance = IocManager.Resolve(handlerType);
                }
                catch
                {
                    if (!IsOptionalHandler(handlerType)) _pendingHandlers.Add(handlerType);
                    continue;
                }

                if (handlerInstance == null)
                {
                    if (!IsOptionalHandler(handlerType)) _pendingHandlers.Add(handlerType);
                    continue;
                }

                var distributedInterfaces = handlerType
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedEventHandler<>))
                .ToArray();

                var allSubscribed = true;
                foreach (var di in distributedInterfaces)
                {
                    var eventType = di.GetGenericArguments()[0];
                    var method = typeof(DistributedEventBusBase).GetMethod(nameof(Subscribe), BindingFlags.Public | BindingFlags.Instance, null, new[] { di }, null);
                    if (method == null)
                    {
                        method = typeof(DistributedEventBusBase)
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == nameof(Subscribe) && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1);
                    }
                    if (method == null)
                    {
                        allSubscribed = false;
                        continue;
                    }

                    try
                    {
                        var genericSubscribe = method.MakeGenericMethod(eventType);
                        var disposableObj = genericSubscribe.Invoke(this, new[] { handlerInstance });
                        if (disposableObj is IDisposable disposable) _autoSubscriptions.Add(disposable);
                    }
                    catch { allSubscribed = false; }
                }

                if (!allSubscribed && !IsOptionalHandler(handlerType)) _pendingHandlers.Add(handlerType); else _pendingHandlers.Remove(handlerType);
            }
            catch { if (!IsOptionalHandler(handlerType)) _pendingHandlers.Add(handlerType); }
        }
    }

    private void RetryPendingHandlers()
    {
        if (_pendingHandlers.Count == 0) return;
        var snapshot = _pendingHandlers.ToArray();
        foreach (var handlerType in snapshot)
        {
            if (IsOptionalHandler(handlerType)) { _pendingHandlers.Remove(handlerType); continue; }
            try
            {
                if (!IocManager.IsRegistered(handlerType)) continue;
                var instance = IocManager.Resolve(handlerType);
                if (instance == null) continue;

                var distributedInterfaces = handlerType
                .GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedEventHandler<>))
                .ToArray();

                var allSubscribed = true;
                foreach (var di in distributedInterfaces)
                {
                    var eventType = di.GetGenericArguments()[0];
                    var method = typeof(DistributedEventBusBase).GetMethod(nameof(Subscribe), BindingFlags.Public | BindingFlags.Instance, null, new[] { di }, null);
                    if (method == null)
                    {
                        method = typeof(DistributedEventBusBase)
                        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == nameof(Subscribe) && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1);
                    }
                    if (method == null)
                    {
                        allSubscribed = false;
                        continue;
                    }
                    try
                    {
                        var genericSubscribe = method.MakeGenericMethod(eventType);
                        var disposableObj = genericSubscribe.Invoke(this, new[] { instance });
                        if (disposableObj is IDisposable d) _autoSubscriptions.Add(d);
                    }
                    catch { allSubscribed = false; }
                }
                if (allSubscribed)
                {
                    _pendingHandlers.Remove(handlerType);
                }
            }
            catch { }
        }
    }

    private void EnsureSubscriptionsInitialized()
    {
        if (!_initialSubscriptionsAttempted)
        {
            TryAutoSubscribeRegisteredHandlers();
        }
        RetryPendingHandlers();
    }

    public virtual Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true) where TEvent : class
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        EnsureSubscriptionsInitialized();
        return PublishAsync(typeof(TEvent), eventData, onUnitOfWorkComplete, useOutbox);
    }

    public virtual async Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    {
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));
        EnsureSubscriptionsInitialized();

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
            // No configured outboxes for this event -> publish directly
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
            if (outbox == null)
            {
                continue;
            }

            var outgoing = new OutgoingEventInfo(
            Guid.NewGuid(),
            typeIdentifier,
            bytes,
            DateTime.UtcNow);

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
                if (created != null)
                {
                    _outboxCache[name] = created;
                    return created;
                }
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
        // Polymorphic dispatch: deliver to handlers registered for the concrete type AND any base types.
        // This enables a handler subscribed to a base class (e.g. BackgroundJobEventBase) to receive all derived events
        // without needing explicit interface registrations per subclass. Interfaces are ignored for now to keep scope minimal.
        var collected = new List<Func<object, Task>>();
        var current = eventType;
        while (current != null && current != typeof(object))
        {
            if (_handlers.TryGetValue(current, out var list) && list.Count > 0)
            {
                collected.AddRange(list);
            }
            current = current.BaseType;
        }

        if (collected.Count == 0) return Task.CompletedTask;

        // Remove duplicates (same delegate may appear if explicitly subscribed for multiple levels)
        var distinct = collected.Distinct().ToList();
        if (distinct.Count == 1) return distinct[0](eventData);
        return Task.WhenAll(distinct.Select(h => h(eventData)));
    }

    public virtual IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler) where TEvent : class
    {
        EnsureSubscriptionsInitialized();
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

    // ISupportsEventBoxes implementations
    public async Task PublishFromOutboxAsync(OutgoingEventInfo outgoingEvent, OutboxConfig outboxConfig)
    {
        if (outgoingEvent == null) throw new ArgumentNullException(nameof(outgoingEvent));
        EnsureSubscriptionsInitialized();
        var type = _serializer.ResolveType(outgoingEvent.EventName);
        if (type == null) return;
        try
        {
            var obj = _serializer.Deserialize(outgoingEvent.EventData, type);
            if (obj != null)
            {
                await DispatchAsync(type, obj);
            }
        }
        catch
        {
            // swallow for test convenience; real impl could log
        }
    }

    public async Task PublishManyFromOutboxAsync(IEnumerable<OutgoingEventInfo> outgoingEvents, OutboxConfig outboxConfig)
    {
        foreach (var evt in outgoingEvents)
        {
            await PublishFromOutboxAsync(evt, outboxConfig);
        }
    }

    public async Task ProcessFromInboxAsync(IncomingEventInfo incomingEvent, InboxConfig inboxConfig)
    {
        if (incomingEvent == null) throw new ArgumentNullException(nameof(incomingEvent));
        EnsureSubscriptionsInitialized();
        var type = _serializer.ResolveType(incomingEvent.EventName);
        if (type == null) return;
        try
        {
            var obj = _serializer.Deserialize(incomingEvent.EventData, type);
            if (obj != null)
            {
                await DispatchAsync(type, obj);
            }
        }
        catch
        {
            // swallow for test convenience; real impl could log
        }
    }

    private sealed class ActionDisposer : IDisposable
    {
        private readonly Action _dispose;
        public ActionDisposer(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _handlers = ImmutableDictionary<Type, ImmutableList<Func<object, Task>>>.Empty;
                _outboxCache.Clear();
                foreach (var sub in _autoSubscriptions)
                {
                    try { sub.Dispose(); } catch { }
                }
                _autoSubscriptions.Clear();
                _pendingHandlers.Clear();
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
 public ValueTask DisposeAsync()
 {
 Dispose();
 return ValueTask.CompletedTask;
 }
#endif
}
