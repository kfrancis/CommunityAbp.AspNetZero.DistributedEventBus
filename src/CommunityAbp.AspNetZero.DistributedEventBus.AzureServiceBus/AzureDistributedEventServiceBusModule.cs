using System;
using System.Collections.Generic;
using System.Text;
using Abp.Configuration.Startup;
using Abp.Dependency;
using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
{
    [DependsOn(typeof(AspNetZeroDistributedEventBusModule))]
    public class AzureDistributedEventServiceBusModule : AbpModule
    {
        public override void PreInitialize()
        {
            Configuration.ReplaceService<IDistributedEventBus, AzureServiceBusDistributedEventBus>(DependencyLifeStyle.Transient);
        }
    }
}
