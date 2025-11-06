using System;
using System.Text.Json;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using Abp.Dependency;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization
{
    /// <summary>
    /// Default implementation using System.Text.Json and assembly qualified type name identifiers.
    /// </summary>
    public class DefaultEventSerializer : IEventSerializer, ISingletonDependency
    {
        private readonly JsonSerializerOptions _options;

        public DefaultEventSerializer()
        {
            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        public byte[] Serialize(object evt, Type eventType)
        {
            return JsonSerializer.SerializeToUtf8Bytes(evt, eventType, _options);
        }

        public object? Deserialize(ReadOnlySpan<byte> data, Type eventType)
        {
            try
            {
                return JsonSerializer.Deserialize(data, eventType, _options);
            }
            catch
            {
                return null;
            }
        }

        public string GetTypeIdentifier(Type type) => type.AssemblyQualifiedName!;

        public Type? ResolveType(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return null;
            return Type.GetType(identifier, throwOnError: false, ignoreCase: false);
        }
    }
}
