using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore; // ensure DbContext module dependency
using Microsoft.EntityFrameworkCore; // fallback
using Castle.MicroKernel.Registration; // fallback

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

[DependsOn(typeof(AspNetZeroDistributedEventBusModule) /*,typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule)*/)]
public class DistributedEventBusEfModule : AbpModule
{
    public override void PreInitialize()
    {
        var options = IocManager.Resolve<DistributedEventBusOptions>();
        if (options.Outboxes.Count == 0)
        {
            options.Outboxes.Configure("Default", cfg =>
            {
                cfg.ImplementationType = typeof(EfCoreEventOutbox);
                cfg.Selector = _ => true;
            });
        }
        if (options.Inboxes.Count == 0)
        {
            options.Inboxes.Configure("Default", cfg =>
            {
                cfg.ImplementationType = typeof(EfCoreEventInbox);
                cfg.EventSelector = _ => true;
            });
        }

        if (!IocManager.IsRegistered<IEventOutbox>())
            IocManager.Register<IEventOutbox, EfCoreEventOutbox>(DependencyLifeStyle.Transient);
        if (!IocManager.IsRegistered<IEventInbox>())
            IocManager.Register<IEventInbox, EfCoreEventInbox>(DependencyLifeStyle.Transient);
    }
}
