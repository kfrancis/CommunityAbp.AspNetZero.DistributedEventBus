using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public class AspNetZeroDistributedEventBusModule : AbpModule
{
    public override void Initialize()
    {
        IocManager.RegisterAssemblyByConvention(typeof(AspNetZeroDistributedEventBusModule).Assembly);
    }

    public override void PreInitialize()
    {
        // Register configurations
        IocManager.Register<DistributedEventBusOptions>();
        IocManager.Register<AspNetZeroEventBusBoxesOptions>();
    }
}