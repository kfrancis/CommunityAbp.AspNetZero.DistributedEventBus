using System;
using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public class AspNetZeroDistributedEventBusModule : AbpModule
{
    public override void PreInitialize()
    {
        // Ensure options live as singletons so runtime configuration (tests) is visible to bus instances.
        if (!IocManager.IsRegistered<DistributedEventBusOptions>())
        {
            IocManager.Register<DistributedEventBusOptions>(DependencyLifeStyle.Singleton);
        }
        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
        {
            IocManager.Register<AspNetZeroEventBusBoxesOptions>(DependencyLifeStyle.Singleton);
        }
        // Register serializer singleton
        if (!IocManager.IsRegistered<IEventSerializer>())
        {
            IocManager.Register<IEventSerializer, DefaultEventSerializer>(DependencyLifeStyle.Singleton);
        }
        IocManager.Register<IOutboxSender, PollingOutboxSender>(DependencyLifeStyle.Singleton);
        IocManager.Register<IInboxProcessor, PollingInboxProcessor>(DependencyLifeStyle.Singleton);
    }

    public override void Initialize()
    {
        IocManager.RegisterAssemblyByConvention(typeof(AspNetZeroDistributedEventBusModule).Assembly);
    }

    public override void PostInitialize()
    {
        // Auto-discovery/auto-subscribe removed. Handlers must be explicitly subscribed by the application.
        // This avoids resolving optional dependencies (e.g. SignalR hubs) in publisher processes where they are not registered.
    }
}
