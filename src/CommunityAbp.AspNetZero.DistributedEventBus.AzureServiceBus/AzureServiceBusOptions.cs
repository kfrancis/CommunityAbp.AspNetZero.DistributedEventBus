using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;

public enum AzureServiceBusEntityKind
{
    Queue = 0,
    Topic = 1
}

/// <summary>
///     Configuration settings used by <see cref="AzureServiceBusDistributedEventBus"/>.
/// </summary>
public class AzureServiceBusOptions : IAzureServiceBusOptions, IAzureServiceBusEntityKindOptions
{
    /// <summary>
    ///     Connection string for the Service Bus namespace.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    ///     Name of the queue or topic used for events.
    /// </summary>
    public string EntityPath { get; set; } = string.Empty;

    /// <summary>
    ///     Optional subscription name when using topics.
    /// </summary>
    public string? SubscriptionName { get; set; }

    /// <summary>Optional for backwards compatibility; inferred from SubscriptionName when omitted.</summary>
    public AzureServiceBusEntityKind? EntityKind { get; set; }
}

public interface IAzureServiceBusOptions
{
    public string ConnectionString { get; set; }
    public string EntityPath { get; set; }

    public string? SubscriptionName { get; set; }
}

/// <summary>Optional additive configuration for callers that need explicit entity mode.</summary>
public interface IAzureServiceBusEntityKindOptions
{
    AzureServiceBusEntityKind? EntityKind { get; set; }
}
