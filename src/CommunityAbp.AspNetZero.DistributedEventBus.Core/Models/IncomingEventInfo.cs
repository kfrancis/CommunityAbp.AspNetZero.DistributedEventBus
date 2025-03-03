using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>
///     Event information interface for incoming events.
/// </summary>
public interface IIncomingEventInfo
{
    /// <summary>
    ///     Unique identifier of this event.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    ///     Message ID of the event.
    /// </summary>
    string MessageId { get; }

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
///     Event information class for incoming events.
/// </summary>
public class IncomingEventInfo : IIncomingEventInfo
{
    private string _correlationId;

    /// <summary>
    ///     Creates a new IncomingEventInfo object.
    /// </summary>
    public IncomingEventInfo(
        Guid id,
        string messageId,
        string eventName,
        byte[] eventData,
        DateTime creationTime)
    {
        Id = id;
        MessageId = messageId;
        EventName = eventName;
        EventData = eventData;
        CreationTime = creationTime;
    }

    /// <summary>
    ///     Unique identifier of this event.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    ///     Message ID of the event.
    /// </summary>
    public string MessageId { get; }

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

    public virtual IncomingEventInfo SetCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public virtual string GetCorrelationId()
    {
        return _correlationId;
    }
}
