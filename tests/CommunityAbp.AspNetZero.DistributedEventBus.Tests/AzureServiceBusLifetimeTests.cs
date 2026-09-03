using Abp.Modules;
using Abp.TestBase;
using Castle.Core;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

/// <summary>
///     Boots a real ABP container with valid Azure Service Bus options and verifies the bus is registered as a
///     process-wide singleton. No connection is opened: the SDK connects lazily and nothing publishes or subscribes here.
/// </summary>
public class AzureServiceBusLifetimeTests : AbpIntegratedTestBase<AzureServiceBusLifetimeTestModule>
{
    protected override void PreInitialize()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureServiceBus:ConnectionString"] = "Endpoint=sb://localhost;SharedAccessKeyName=Root;SharedAccessKey=key",
                ["AzureServiceBus:EntityPath"] = "distributed-events",
                ["AzureServiceBus:SubscriptionName"] = "mendmd-web",
                ["AzureServiceBus:EntityKind"] = "Topic"
            })
            .Build();
        LocalIocManager.IocContainer.Register(Component.For<IConfiguration>().Instance(configuration).LifestyleSingleton());
    }

    [Fact]
    public void ResolvingBusTwice_ReturnsSameAzureInstance()
    {
        var first = Resolve<IDistributedEventBus>();
        var second = Resolve<IDistributedEventBus>();

        Assert.IsType<AzureServiceBusDistributedEventBus>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Bus_IsRegisteredWithSingletonLifestyle()
    {
        var handler = LocalIocManager.IocContainer.Kernel.GetHandler(typeof(IDistributedEventBus));

        Assert.NotNull(handler);
        Assert.Equal(typeof(AzureServiceBusDistributedEventBus), handler.ComponentModel.Implementation);
        Assert.Equal(LifestyleType.Singleton, handler.ComponentModel.LifestyleType);
    }

    [Fact]
    public void ResolvedBus_HasNoProcessorUntilSubscribed()
    {
        var bus = Assert.IsType<AzureServiceBusDistributedEventBus>(Resolve<IDistributedEventBus>());
        Assert.False(bus.HasActiveProcessor);
    }
}

[DependsOn(typeof(AbpTestBaseModule), typeof(AzureDistributedEventServiceBusModule))]
public class AzureServiceBusLifetimeTestModule : AbpModule
{
}
