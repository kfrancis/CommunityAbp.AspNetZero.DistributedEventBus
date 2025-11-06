using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

[DependsOn(typeof(AspNetZeroDistributedEventBusModule))]
public class DistributedEventBusEfModule : AbpModule
{
    public override void PreInitialize()
    {
        var options = IocManager.Resolve<DistributedEventBusOptions>();
        options.Outboxes.Configure("Default", cfg =>
        {
            cfg.ImplementationType = typeof(EfCoreEventOutbox);
            cfg.Selector = _ => true;
        });
        options.Inboxes.Configure("Default", cfg =>
        {
            cfg.ImplementationType = typeof(EfCoreEventInbox);
            cfg.EventSelector = _ => true;
        });

        IocManager.Register<IEventOutbox, EfCoreEventOutbox>(DependencyLifeStyle.Transient);
        IocManager.Register<IEventInbox, EfCoreEventInbox>(DependencyLifeStyle.Transient);
    }
}
