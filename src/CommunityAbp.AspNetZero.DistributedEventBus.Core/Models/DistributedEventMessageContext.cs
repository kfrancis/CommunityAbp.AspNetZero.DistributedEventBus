using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>Non-payload metadata for the event currently being dispatched.</summary>
public sealed class DistributedEventMessageContext
{
    public string MessageId { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string? LegacyTypeIdentifier { get; set; }
    public string? EntityPath { get; set; }
    public string? SubscriptionName { get; set; }
    public long? DeliveryCount { get; set; }
    public string? CorrelationId { get; set; }
    public DistributedEventDispatchMode DispatchMode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
