using Abp.Configuration.Startup;
using Abp.Dependency;
using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using System.IO;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
{
    [DependsOn(typeof(AspNetZeroDistributedEventBusModule))]
    public class AzureDistributedEventServiceBusModule : AbpModule
    {
        public override void PreInitialize()
        {
            var configuration = GetConfiguration();
            Configuration.ReplaceService<IDistributedEventBus, AzureServiceBusDistributedEventBus>(DependencyLifeStyle.Transient);

            IocManager.IocContainer.Register(
                Component.For<AzureServiceBusOptions>().Instance(GetAzureOptions(configuration)).LifestyleSingleton()
            );
        }

        private static IConfigurationRoot GetConfiguration()
        {
            return AppConfigurations.Get(Directory.GetCurrentDirectory(), addUserSecrets: true);
        }

        private AzureServiceBusOptions GetAzureOptions(IConfigurationRoot configuration)
        {
            var options = new AzureServiceBusOptions();
            configuration.GetSection("AzureServiceBus").Bind(options);
            return options;
        }
    }
}
