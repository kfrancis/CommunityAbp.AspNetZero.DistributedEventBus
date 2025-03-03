namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>
///     Sources of distributed events.
/// </summary>
public enum DistributedEventSource
{
    /// <summary>
    ///     Event is directly published.
    /// </summary>
    Direct,

    /// <summary>
    ///     Event is published from an outbox.
    /// </summary>
    Outbox,

    /// <summary>
    ///     Event is received from an inbox.
    /// </summary>
    Inbox
}

/// <summary>
///     Event data for distributed event sent.
/// </summary>
public class DistributedEventSent
{
    /// <summary>
    ///     Source of the event.
    /// </summary>
    public DistributedEventSource Source { get; set; }

    /// <summary>
    ///     Name of the event.
    /// </summary>
    public string EventName { get; set; }

    /// <summary>
    ///     Event data object.
    /// </summary>
    public object EventData { get; set; }
}

/// <summary>
///     Event data for distributed event received.
/// </summary>
public class DistributedEventReceived
{
    /// <summary>
    ///     Source of the event.
    /// </summary>
    public DistributedEventSource Source { get; set; }

    /// <summary>
    ///     Name of the event.
    /// </summary>
    public string EventName { get; set; }

    /// <summary>
    ///     Event data object.
    /// </summary>
    public object EventData { get; set; }
}
