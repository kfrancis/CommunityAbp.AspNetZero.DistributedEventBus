using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base
{
    // Simple in-memory implementation for testing
    public class InMemoryDistributedEventBus : IDistributedEventBus
    {
        private readonly ConcurrentDictionary<Type, List<Func<object, Task>>> _handlers = new();

        public Task PublishAsync<TEvent>(TEvent eventData, bool useOutbox = false) where TEvent : class
        {
            if (eventData == null)
                throw new ArgumentNullException(nameof(eventData));

            var type = typeof(TEvent);
            if (_handlers.TryGetValue(type, out var handlers))
            {
                var tasks = new List<Task>();
                foreach (var handler in handlers)
                {
                    tasks.Add(handler(eventData));
                }
                return Task.WhenAll(tasks);
            }
            return Task.CompletedTask;
        }

        public void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            var type = typeof(TEvent);
            _handlers.AddOrUpdate(
                type,
                _ => new List<Func<object, Task>> { e => handler((TEvent)e) },
                (_, list) =>
                {
                    list.Add(e => handler((TEvent)e));
                    return list;
                });
        }

        public void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            var type = typeof(TEvent);
            if (_handlers.TryGetValue(type, out var handlers))
            {
                handlers.RemoveAll(h => h == (Func<object, Task>)(e => handler((TEvent)e)));
            }
        }
    }
}
