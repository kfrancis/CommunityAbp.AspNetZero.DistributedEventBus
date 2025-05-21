using Abp.Configuration.Startup;
using Abp.Dependency;
using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
{
    [DependsOn(typeof(AspNetZeroDistributedEventBusModule))]
    public class AzureDistributedEventServiceBusModule : AbpModule
    {
        public override void PreInitialize()
        {
            Configuration.ReplaceService<IDistributedEventBus, AzureServiceBusDistributedEventBus>(DependencyLifeStyle.Transient);

            IocManager.IocContainer.Register(
                Component.For<AzureServiceBusOptions>().Instance(GetAzureOptions()).LifestyleSingleton()
            );
        }

        private AzureServiceBusOptions GetAzureOptions()
        {
            var configuration = IocManager.Resolve<IConfiguration>();
            var options = new AzureServiceBusOptions();
            configuration.GetSection("AzureServiceBus").Bind(options);
            return options;
        }
    }
}
