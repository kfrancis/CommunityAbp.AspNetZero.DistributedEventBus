using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>
///     Event information interface for outgoing events.
/// </summary>
public interface IOutgoingEventInfo
{
    /// <summary>
    ///     Unique identifier of this event.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    ///     Name of the event.
    /// </summary>
    string EventName { get; }

    /// <summary>
    ///     Serialized event data as a byte array.
    /// </summary>
    byte[] EventData { get; }

    /// <summary>
    ///     Creation time of this event.
    /// </summary>
    DateTime CreationTime { get; }
}

/// <summary>
///     Event information class for outgoing events.
/// </summary>
public class OutgoingEventInfo : IOutgoingEventInfo
{
    private string _correlationId;

    /// <summary>
    ///     Creates a new OutgoingEventInfo object.
    /// </summary>
    public OutgoingEventInfo(
        Guid id,
        string eventName,
        byte[] eventData,
        DateTime creationTime)
    {
        Id = id;
        EventName = eventName;
        EventData = eventData;
        CreationTime = creationTime;
    }

    /// <summary>
    ///     Unique identifier of this event.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     Name of the event.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    ///     Serialized event data as a byte array.
    /// </summary>
    public byte[] EventData { get; }

    /// <summary>
    ///     Creation time of this event.
    /// </summary>
    public DateTime CreationTime { get; }

    public virtual OutgoingEventInfo SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public virtual string GetCorrelationId()
    {
        return _correlationId;
    }
}
