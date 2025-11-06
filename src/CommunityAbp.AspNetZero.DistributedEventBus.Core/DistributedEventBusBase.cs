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
using Castle.MicroKernel.Registration;
using System.Reflection;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

#if NETSTANDARD2_0
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISupportsEventBoxes, IDisposable
#else
public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISUPPORTSEventBoxes, IDisposable, IAsyncDisposable
#endif
{
    private readonly Dictionary<Type, List<Func<object, Task>>> _handlers = new();
    private bool _disposed;

    private DistributedEventBusOptions? _options; // cached options instance

    // Cache resolved outboxes by configured name
    private readonly Dictionary<string, IEventOutbox> _outboxCache = new(StringComparer.OrdinalIgnoreCase);

    public DistributedEventBusBase(DistributedEventBusOptions? options = null)
    {
        _options = options;
    }

    private DistributedEventBusOptions? GetOptions()
    {
        if (_options != null) return _options;
        try
        {
            if (IocManager.Instance.IsRegistered<DistributedEventBusOptions>())
            {
                _options = IocManager.Instance.Resolve<DistributedEventBusOptions>();
            }
        }
        catch { }
        return _options;
    }

    public virtual Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true) where TEvent : class
    {
        if (eventData == null) throw new ArgumentNullException(nameof(eventData));
        return PublishAsync(typeof(TEvent), eventData, onUnitOfWorkComplete, useOutbox);
    }

    public virtual async Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    {
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));

        if (!useOutbox)
        {
            await DispatchAsync(eventType, eventData);
            return;
        }

        var options = GetOptions();
        if (options == null)
        {
            await DispatchAsync(eventType, eventData);
            return;
        }

        var matchingOutboxes = options.Outboxes
            .Where(kv => kv.Value.IsSendingEnabled && (kv.Value.Selector == null || kv.Value.Selector(eventType)))
            .ToList();

        if (matchingOutboxes.Count == 0)
        {
            await DispatchAsync(eventType, eventData);
            return;
        }

        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(eventData);

        foreach (var kv in matchingOutboxes)
        {
            var name = kv.Key;
            var outboxConfig = kv.Value;
            var outbox = ResolveOutbox(name, outboxConfig);
            if (outbox == null)
            {
                await DispatchAsync(eventType, eventData); // fail-safe
                continue;
            }

            var outgoing = new OutgoingEventInfo(
                Guid.NewGuid(),
                eventType.AssemblyQualifiedName!,
                serializedBytes,
                DateTime.UtcNow);
            try
            {
                await outbox.AddAsync(outgoing, CancellationToken.None);
            }
            catch
            {
                await DispatchAsync(eventType, eventData); // fail-safe
            }
        }
    }

    private IEventOutbox? ResolveOutbox(string name, OutboxConfig config)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = config.ImplementationType?.FullName ?? Guid.NewGuid().ToString("N");
        }

        if (_outboxCache.TryGetValue(name, out var cached)) return cached;

        // Factory override path
        if (config.Factory != null)
        {
            try
            {
                var created = config.Factory(IocManager.Instance, config);
                if (created != null)
                {
                    _outboxCache[name] = created;
                    return created;
                }
            }
            catch { /* ignore and continue */ }
        }

        if (config.ImplementationType != null && typeof(IEventOutbox).IsAssignableFrom(config.ImplementationType))
        {
            // EARLY interface-based resolution: interface may be registered but concrete type not explicitly
            if (!IocManager.Instance.IsRegistered(config.ImplementationType) && IocManager.Instance.IsRegistered<IEventOutbox>())
            {
                try
                {
                    var candidate = IocManager.Instance.Resolve<IEventOutbox>();
                    if (candidate != null && config.ImplementationType.IsInstanceOfType(candidate))
                    {
                        _outboxCache[name] = candidate;
                        return candidate;
                    }
                }
                catch { /* continue */ }
            }

            // If concrete already registered simply resolve
            if (IocManager.Instance.IsRegistered(config.ImplementationType))
            {
                try
                {
                    var resolvedExisting = (IEventOutbox)IocManager.Instance.Resolve(config.ImplementationType);
                    _outboxCache[name] = resolvedExisting;
                    return resolvedExisting;
                }
                catch { /* continue to fallback */ }
            }
            else
            {
                var ctor = config.ImplementationType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();
                if (ctor != null)
                {
                    var parameters = ctor.GetParameters();
                    var allResolvable = parameters.All(p => IocManager.Instance.IsRegistered(p.ParameterType));
                    if (allResolvable && parameters.Length > 0)
                    {
                        try
                        {
                            IocManager.Instance.IocContainer.Register(
                                Component.For(typeof(IEventOutbox), config.ImplementationType)
                                         .ImplementedBy(config.ImplementationType)
                                         .LifestyleSingleton());
                            var resolved = (IEventOutbox)IocManager.Instance.Resolve(config.ImplementationType);
                            _outboxCache[name] = resolved;
                            return resolved;
                        }
                        catch { /* ignore */ }
                    }
                    else if (parameters.Length == 0)
                    {
                        try
                        {
                            IocManager.Instance.IocContainer.Register(
                                Component.For(typeof(IEventOutbox), config.ImplementationType)
                                         .ImplementedBy(config.ImplementationType)
                                         .LifestyleSingleton());
                            var resolved = (IEventOutbox)IocManager.Instance.Resolve(config.ImplementationType);
                            _outboxCache[name] = resolved;
                            return resolved;
                        }
                        catch { /* ignore */ }
                    }
                }
            }
        }

        try
        {
            if (IocManager.Instance.IsRegistered<IEventOutbox>())
            {
                var handlers = IocManager.Instance.IocContainer.Kernel.GetHandlers(typeof(IEventOutbox));
                if (handlers.Length == 1)
                {
                    var singleton = IocManager.Instance.Resolve<IEventOutbox>();
                    _outboxCache[name] = singleton;
                    return singleton;
                }
            }
        }
        catch { }

        return null;
    }

    private IEventOutbox? ResolveOutbox(OutboxConfig config) =>
        ResolveOutbox(config.ImplementationType?.FullName ?? Guid.NewGuid().ToString("N"), config);

    private Task DispatchAsync(Type eventType, object eventData)
    {
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            return Task.WhenAll(handlers.Select(h => h(eventData)));
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
        if (eventType == null) return Task.CompletedTask;
        var eventData = JsonSerializer.Deserialize(outgoingEvent.EventData, eventType);
        return eventData != null ? DispatchAsync(eventType, eventData) : Task.CompletedTask;
    }

    public virtual Task PublishManyFromOutboxAsync(IEnumerable<OutgoingEventInfo> outgoingEvents, OutboxConfig outboxConfig)
        => Task.WhenAll(outgoingEvents.Select(e => PublishFromOutboxAsync(e, outboxConfig)));

    public virtual Task ProcessFromInboxAsync(IncomingEventInfo incomingEvent, InboxConfig inboxConfig)
    {
        var eventType = Type.GetType(incomingEvent.EventName);
        if (eventType == null) return Task.CompletedTask;
        var eventData = JsonSerializer.Deserialize(incomingEvent.EventData, eventType);
        return eventData != null ? DispatchAsync(eventType, eventData) : Task.CompletedTask;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _handlers.Clear();
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
        => PublishAsync(eventData, onUnitOfWorkComplete, useOutbox);

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
