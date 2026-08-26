using System;
using System.Collections.Concurrent;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public sealed class EventTypeRegistry : IEventTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _types = new(StringComparer.Ordinal);

    public void Register<TEvent>(params string[] aliases) where TEvent : class => Register(typeof(TEvent), aliases);

    public void Register(Type eventType, params string[] aliases)
    {
        if (eventType is null) throw new ArgumentNullException(nameof(eventType));

        Add(GetEventName(eventType), eventType);
        if (!string.IsNullOrWhiteSpace(eventType.AssemblyQualifiedName)) Add(eventType.AssemblyQualifiedName, eventType);
        if (!string.IsNullOrWhiteSpace(eventType.FullName)) Add(eventType.FullName, eventType);
        foreach (var alias in aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias)) Add(alias, eventType);
        }
    }

    public string GetEventName(Type eventType) => EventNameAttribute.GetNameOrDefault(eventType);

    public Type? Resolve(string identifier) =>
        string.IsNullOrWhiteSpace(identifier) ? null : _types.TryGetValue(identifier, out var type) ? type : null;

    private void Add(string identifier, Type eventType)
    {
        if (!_types.TryAdd(identifier, eventType) && _types[identifier] != eventType)
        {
            throw new InvalidOperationException($"The event identifier '{identifier}' is already registered for a different type.");
        }
    }
}
