namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>Controls whether an event is sent immediately or persisted for outbox delivery.</summary>
public enum DistributedEventDispatchMode
{
    Direct = 0,
    Outbox = 1
}
