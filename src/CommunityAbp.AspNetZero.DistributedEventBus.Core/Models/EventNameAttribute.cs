using System;
using System.Reflection;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>
///     Used to define the name of an event.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class EventNameAttribute : Attribute
{
    /// <summary>
    ///     Creates a new EventNameAttribute with the specified name.
    /// </summary>
    /// <param name="name">The name of the event</param>
    public EventNameAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    ///     The name of the event.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the event name for the given type.
    ///     Uses EventNameAttribute if defined, otherwise returns the full type name.
    /// </summary>
    /// <param name="eventType">The type of the event</param>
    /// <returns>The name of the event</returns>
    public static string GetNameOrDefault(Type eventType)
    {
        if (eventType == null) throw new ArgumentNullException(nameof(eventType));

        var eventNameAttribute = eventType.GetCustomAttribute<EventNameAttribute>();
        return eventNameAttribute == null ? eventType.FullName : eventNameAttribute.Name;
    }
}