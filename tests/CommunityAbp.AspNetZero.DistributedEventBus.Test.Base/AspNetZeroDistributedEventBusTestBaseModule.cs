using Abp.Modules;
using Abp.TestBase;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NSubstitute;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

[DependsOn(typeof(AbpTestBaseModule), typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule))]
public class AspNetZeroDistributedEventBusTestBaseModule : AbpModule
{
    public AspNetZeroDistributedEventBusTestBaseModule(AspNetZeroDistributedEventEntityFrameworkCoreModule aspNetZeroDistributedEventEntityFrameworkCoreModule)
    {
        aspNetZeroDistributedEventEntityFrameworkCoreModule.SkipDbContextRegistration = true;
    }

    public override void PreInitialize()
    {
        var configuration = GetConfiguration();

        Configuration.BackgroundJobs.IsJobExecutionEnabled = false;

        Configuration.UnitOfWork.Timeout = TimeSpan.FromMinutes(30);
        Configuration.UnitOfWork.IsTransactional = false;
    }

    public override void Initialize()
    {
        ServiceCollectionRegistrar.Register(IocManager);
    }

    private void RegisterFakeService<TService>()
        where TService : class
    {
        IocManager.IocContainer.Register(
            Component.For<TService>()
                .UsingFactoryMethod(() => Substitute.For<TService>())
                .LifestyleSingleton()
        );
    }

    private static IConfigurationRoot GetConfiguration()
    {
        return AppConfigurations.Get(Directory.GetCurrentDirectory(), addUserSecrets: true);
    }
}
