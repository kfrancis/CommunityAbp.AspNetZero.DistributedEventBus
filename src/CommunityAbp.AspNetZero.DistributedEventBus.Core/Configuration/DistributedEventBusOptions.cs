using Abp.Collections;
using Abp.Dependency;
using Abp.Events.Bus.Handlers;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;

public class DistributedEventBusOptions : ISingletonDependency
{
    public ITypeList<IEventHandler> Handlers { get; } = new TypeList<IEventHandler>();
    public OutboxConfigDictionary Outboxes { get; } = new();
    public InboxConfigDictionary Inboxes { get; } = new();
}
