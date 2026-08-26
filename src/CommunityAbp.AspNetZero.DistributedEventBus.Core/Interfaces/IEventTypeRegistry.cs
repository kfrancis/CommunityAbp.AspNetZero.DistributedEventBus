using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>Maps stable logical event names and compatibility aliases to CLR event types.</summary>
public interface IEventTypeRegistry
{
    void Register<TEvent>(params string[] aliases) where TEvent : class;
    void Register(Type eventType, params string[] aliases);
    string GetEventName(Type eventType);
    Type? Resolve(string identifier);
}
