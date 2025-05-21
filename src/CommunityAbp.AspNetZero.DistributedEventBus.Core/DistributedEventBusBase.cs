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

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public class DistributedEventBusBase : EventBus, IDistributedEventBus, ISupportsEventBoxes
{
    private readonly Dictionary<Type, List<Func<object, Task>>> _handlers = new();

    public virtual Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
        where TEvent : class
    {
        return PublishAsync(typeof(TEvent), eventData, onUnitOfWorkComplete, useOutbox);
    }

    public virtual Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
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

        return new DisposeAction(() => handlers.Remove(Wrapper));
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
            ? PublishAsync(eventType, eventData)
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
            ? PublishAsync(eventType, eventData)
            : Task.CompletedTask;
    }
}
