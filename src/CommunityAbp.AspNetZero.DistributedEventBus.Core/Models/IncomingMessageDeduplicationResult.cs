namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

/// <summary>Result of acquiring ownership for an incoming broker message.</summary>
public enum IncomingMessageDeduplicationResult
{
    /// <summary>The caller owns processing and must complete or abandon the lease.</summary>
    Acquired = 0,

    /// <summary>The message was processed previously and can be broker-completed.</summary>
    AlreadyCompleted = 1,

    /// <summary>Another processor currently owns the message; it must be abandoned.</summary>
    InProgress = 2
}
