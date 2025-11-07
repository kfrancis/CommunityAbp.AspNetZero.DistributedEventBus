using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Abp.Dependency;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

[DependsOn(typeof(AspNetZeroDistributedEventBusTestBaseModule))]
public class DistributedEventBusTestModule : AbpModule
{
    public override void PreInitialize()
    {
        // Register auto subscription test handler type into options so bus will auto-subscribe.
        var options = IocManager.Resolve<DistributedEventBusOptions>();
        if (!options.Handlers.Contains(typeof(AutoTestEventHandler)))
        {
            options.Handlers.Add(typeof(AutoTestEventHandler));
        }
        // IMPORTANT: Must be singleton so the instance we assert against is the same one auto-subscribed.
        if (!IocManager.IsRegistered<AutoTestEventHandler>())
        {
            IocManager.Register<AutoTestEventHandler>(DependencyLifeStyle.Singleton);
        }
    }
}
