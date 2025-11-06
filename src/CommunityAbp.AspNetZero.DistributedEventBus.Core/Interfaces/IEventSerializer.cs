using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces
{
    /// <summary>
    /// Abstraction for event serialization, type identification and resolution.
    /// </summary>
    public interface IEventSerializer
    {
        byte[] Serialize(object evt, Type eventType);
        object? Deserialize(ReadOnlySpan<byte> data, Type eventType);
        string GetTypeIdentifier(Type type);
        Type? ResolveType(string identifier);
    }
}
