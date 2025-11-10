using System;
using Abp;
using Abp.Configuration.Startup;
using Abp.Dependency;
using Abp.Modules;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
{
    [DependsOn(typeof(AspNetZeroDistributedEventBusModule))]
    public class AzureDistributedEventServiceBusModule : AbpModule
    {
        private IConfiguration? _configuration;

        public override void PreInitialize()
        {
            // Try to resolve IConfiguration from existing host; fallback if not available.
            if (IocManager.IsRegistered<IConfiguration>())
            {
                _configuration = IocManager.Resolve<IConfiguration>();
            }

            var options = LoadOptions(_configuration);

            // Only wire up Azure Service Bus implementation if we have a valid configuration.
            if (IsValidOptions(options))
            {
                // Enforce validation (throws) when attempting to actually use Azure Service Bus.
                ValidateOptions(options);

                Configuration.ReplaceService<IDistributedEventBus, AzureServiceBusDistributedEventBus>(DependencyLifeStyle.Transient);

                IocManager.IocContainer.Register(
                    Component.For<IAzureServiceBusOptions>().Instance(options).LifestyleSingleton()
                );
            }
            else
            {
                // When invalid (e.g. test environment without config), skip replacing the bus.
                // The test base module (or application) can register a different implementation.
            }
        }

        private AzureServiceBusOptions LoadOptions(IConfiguration? cfg)
        {
            var options = new AzureServiceBusOptions();
            if (cfg == null)
            {
                // Fallback: build a minimal configuration from current base directory.
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
                var builder = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile($"appsettings.{env}.json", optional: true)
                    .AddEnvironmentVariables();
                cfg = builder.Build();
            }
            cfg.GetSection("AzureServiceBus").Bind(options);
            return options;
        }

        private static bool IsValidOptions(AzureServiceBusOptions opts)
        {
            return !string.IsNullOrWhiteSpace(opts.ConnectionString) && !string.IsNullOrWhiteSpace(opts.EntityPath);
        }

        private void ValidateOptions(AzureServiceBusOptions opts)
        {
            // Only enforce ConnectionString + EntityPath (SubscriptionName optional for queue scenario)
            if (string.IsNullOrWhiteSpace(opts.ConnectionString))
            {
                throw new AbpException("AzureServiceBus:ConnectionString is missing or empty.");
            }
            if (string.IsNullOrWhiteSpace(opts.EntityPath))
            {
                throw new AbpException("AzureServiceBus:EntityPath is missing or empty.");
            }
        }
    }
}
