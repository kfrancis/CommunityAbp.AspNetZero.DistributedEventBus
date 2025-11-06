using Abp.Dependency;
using Abp.Modules;
using Abp.TestBase;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base.DependencyInjection;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

[DependsOn(
    typeof(AbpTestBaseModule),
    typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule),
    typeof(AzureDistributedEventServiceBusModule)
)]
public class AspNetZeroDistributedEventBusTestBaseModule : AbpModule
{
    public AspNetZeroDistributedEventBusTestBaseModule(AspNetZeroDistributedEventEntityFrameworkCoreModule aspNetZeroDistributedEventEntityFrameworkCoreModule)
    {
        aspNetZeroDistributedEventEntityFrameworkCoreModule.SkipDbContextRegistration = true;
    }

    public override void PreInitialize()
    {
        Configuration.BackgroundJobs.IsJobExecutionEnabled = false;
        Configuration.UnitOfWork.Timeout = TimeSpan.FromMinutes(30);
        Configuration.UnitOfWork.IsTransactional = false;

        // Ensure options objects are registered before configuring.
        if (!IocManager.IsRegistered<DistributedEventBusOptions>())
        {
            IocManager.Register<DistributedEventBusOptions>(DependencyLifeStyle.Singleton);
        }
        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
        {
            IocManager.Register<AspNetZeroEventBusBoxesOptions>(DependencyLifeStyle.Singleton);
        }

        // Configure a default outbox using EF Core implementation.
        var options = IocManager.Resolve<DistributedEventBusOptions>();
        options.Outboxes.Configure("Default", o =>
        {
            o.DatabaseName = "Default";
            o.ImplementationType = typeof(EfCoreEventOutbox);
            o.Selector = _ => true; // capture all events
            o.IsSendingEnabled = true;
        });

        // Ensure IEventOutbox is registered only once mapping to EfCoreEventOutbox.
        if (!IocManager.IsRegistered<IEventOutbox>())
        {
            IocManager.Register<IEventOutbox, EfCoreEventOutbox>(DependencyLifeStyle.Transient);
        }

        if (!IocManager.IsRegistered<IDistributedEventBus>())
        {
            IocManager.Register<IDistributedEventBus, DistributedEventBusBase>(DependencyLifeStyle.Transient);
        }
    }

    public override void Initialize()
    {
        ServiceCollectionRegistrar.Register(IocManager);
    }

}
