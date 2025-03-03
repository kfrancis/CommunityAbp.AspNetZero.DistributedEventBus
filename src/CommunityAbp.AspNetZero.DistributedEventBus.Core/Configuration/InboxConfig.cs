using System;
using System.Collections.Generic;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;

/// <summary>
///     Configuration options for event inboxes.
/// </summary>
public class InboxConfig
{
    /// <summary>
    ///     Creates a new InboxConfig with default values.
    /// </summary>
    public InboxConfig()
    {
        DatabaseName = "Default";
        IsProcessingEnabled = true;
    }

    /// <summary>
    ///     Database name used for inbox storage.
    /// </summary>
    public string DatabaseName { get; set; }

    /// <summary>
    ///     Type implementing IEventInbox.
    /// </summary>
    public Type ImplementationType { get; set; }

    /// <summary>
    ///     A predicate to filter events to be stored in this inbox.
    /// </summary>
    public Func<Type, bool> EventSelector { get; set; }

    /// <summary>
    ///     True to enable processing inbox events, false to disable.
    /// </summary>
    public bool IsProcessingEnabled { get; set; }
}

/// <summary>
///     A dictionary of inbox configurations.
/// </summary>
public class InboxConfigDictionary : Dictionary<string, InboxConfig>
{
    /// <summary>
    ///     Configures an inbox with the given name.
    /// </summary>
    /// <param name="name">Unique name of the inbox</param>
    /// <param name="configureAction">Action to configure the inbox</param>
    public void Configure(string name, Action<InboxConfig> configureAction)
    {
        var configuration = GetOrAdd(name, () => new InboxConfig());
        configureAction(configuration);
    }

    /// <summary>
    ///     Gets or adds an inbox configuration with the given name.
    /// </summary>
    /// <param name="name">Unique name of the inbox</param>
    /// <param name="factory">Factory to create the configuration if it doesn't exist</param>
    /// <returns>The inbox configuration</returns>
    public InboxConfig GetOrAdd(string name, Func<InboxConfig> factory)
    {
        return TryGetValue(name, out var obj)
            ? obj
            : this[name] = factory();
    }
}
