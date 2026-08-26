using System;
using System.Collections.Generic;
using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public class AspNetZeroDistributedEventBusModule : AbpModule
{
    private readonly List<IDisposable> _lifecycleSubscriptions = [];
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
        if (!IocManager.IsRegistered<DistributedEventSubscriptionOptions>())
        {
            IocManager.Register<DistributedEventSubscriptionOptions>(DependencyLifeStyle.Singleton);
        }
        if (!IocManager.IsRegistered<IEventTypeRegistry>())
        {
            IocManager.Register<IEventTypeRegistry, EventTypeRegistry>(DependencyLifeStyle.Singleton);
        }
        if (!IocManager.IsRegistered<IDistributedEventContextAccessor>())
        {
            IocManager.Register<IDistributedEventContextAccessor, DistributedEventContextAccessor>(DependencyLifeStyle.Singleton);
        }
        IocManager.Register<IOutboxSender, PollingOutboxSender>(DependencyLifeStyle.Transient);
        IocManager.Register<IInboxProcessor, PollingInboxProcessor>(DependencyLifeStyle.Transient);
    }

    public override void Initialize()
    {
        IocManager.RegisterAssemblyByConvention(typeof(AspNetZeroDistributedEventBusModule).Assembly);
    }

    public override void PostInitialize()
    {
        var registrations = IocManager.Resolve<DistributedEventSubscriptionOptions>().Registrations;
        if (registrations.Count == 0) return;
        var bus = IocManager.Resolve<IDistributedEventBus>();
        foreach (var registration in registrations) _lifecycleSubscriptions.Add(registration(IocManager, bus));
    }

    public override void Shutdown()
    {
        foreach (var subscription in _lifecycleSubscriptions) subscription.Dispose();
        _lifecycleSubscriptions.Clear();
    }
}
