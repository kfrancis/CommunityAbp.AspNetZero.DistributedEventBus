namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;

/// <summary>
///     Additional options for the distributed event bus integration.
/// </summary>
public class AspNetZeroDistributedEventBusOptions
{
    /// <summary>
    ///     Specifies whether messages should be processed at-least-once or exactly-once.
    /// </summary>
    public bool EnableExactlyOnceDelivery { get; set; }
}
