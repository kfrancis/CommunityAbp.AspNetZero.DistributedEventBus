using System;
using System.Collections.Generic;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;

/// <summary>
///     Configuration options for event outboxes.
/// </summary>
public class OutboxConfig
{
    /// <summary>
    ///     Creates a new OutboxConfig with default values.
    /// </summary>
    public OutboxConfig()
    {
        DatabaseName = "Default";
        IsSendingEnabled = true;
    }

    /// <summary>
    ///     Database name used for outbox storage.
    /// </summary>
    public string DatabaseName { get; set; }

    /// <summary>
    ///     Type implementing IEventOutbox.
    /// </summary>
    public Type ImplementationType { get; set; }

    /// <summary>
    ///     A predicate to filter events to be stored in this outbox.
    /// </summary>
    public Func<Type, bool> Selector { get; set; }

    /// <summary>
    ///     True to enable sending outbox events, false to disable.
    /// </summary>
    public bool IsSendingEnabled { get; set; }
}

/// <summary>
///     A dictionary of outbox configurations.
/// </summary>
public class OutboxConfigDictionary : Dictionary<string, OutboxConfig>
{
    /// <summary>
    ///     Configures an outbox with the given name.
    /// </summary>
    /// <param name="name">Unique name of the outbox</param>
    /// <param name="configureAction">Action to configure the outbox</param>
    public void Configure(string name, Action<OutboxConfig> configureAction)
    {
        var configuration = GetOrAdd(name, () => new OutboxConfig());
        configureAction(configuration);
    }

    /// <summary>
    ///     Gets or adds an outbox configuration with the given name.
    /// </summary>
    /// <param name="name">Unique name of the outbox</param>
    /// <param name="factory">Factory to create the configuration if it doesn't exist</param>
    /// <returns>The outbox configuration</returns>
    public OutboxConfig GetOrAdd(string name, Func<OutboxConfig> factory)
    {
        return TryGetValue(name, out var obj)
            ? obj
            : this[name] = factory();
    }
}
