using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public class AspNetZeroDistributedEventBusModule : AbpModule
{
    public override void PreInitialize()
    {
        IocManager.Register<DistributedEventBusOptions>();
        IocManager.Register<AspNetZeroEventBusBoxesOptions>();
        IocManager.Register<IOutboxSender, PollingOutboxSender>(DependencyLifeStyle.Singleton);
        IocManager.Register<IInboxProcessor, PollingInboxProcessor>(DependencyLifeStyle.Singleton);
    }

    public override void Initialize()
    {
        IocManager.RegisterAssemblyByConvention(typeof(AspNetZeroDistributedEventBusModule).Assembly);
    }
}
