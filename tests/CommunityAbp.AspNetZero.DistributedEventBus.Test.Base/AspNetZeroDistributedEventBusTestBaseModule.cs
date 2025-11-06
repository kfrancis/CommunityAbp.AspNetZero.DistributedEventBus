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
using System;
using Castle.MicroKernel.Registration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

[DependsOn(
    typeof(AbpTestBaseModule),
    typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule),
    typeof(AzureDistributedEventServiceBusModule)
)]
public class AspNetZeroDistributedEventBusTestBaseModule : AbpModule
{
    public AspNetZeroDistributedEventBusTestBaseModule(AspNetZeroDistributedEventEntityFrameworkCoreModule efModule)
    {
        // Skip default registration to control in-memory db manually.
        efModule.SkipDbContextRegistration = true;
    }

    public override void PreInitialize()
    {
        Configuration.BackgroundJobs.IsJobExecutionEnabled = false;
        Configuration.UnitOfWork.Timeout = TimeSpan.FromMinutes(30);
        Configuration.UnitOfWork.IsTransactional = false;

        // Options singletons
        if (!IocManager.IsRegistered<DistributedEventBusOptions>())
            IocManager.Register<DistributedEventBusOptions>(DependencyLifeStyle.Singleton);
        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
            IocManager.Register<AspNetZeroEventBusBoxesOptions>(DependencyLifeStyle.Singleton);

        // In-memory SQLite DbContext registration BEFORE outbox configuration
        if (!IocManager.IsRegistered<DbContextOptions<DistributedEventBusDbContext>>())
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var builder = new DbContextOptionsBuilder<DistributedEventBusDbContext>().UseSqlite(conn);

            IocManager.IocContainer.Register(
                Component.For<DbContextOptions<DistributedEventBusDbContext>>()
                         .Instance(builder.Options)
                         .LifestyleSingleton(),
                Component.For<DistributedEventBusDbContext>()
                         .UsingFactoryMethod(k => new DistributedEventBusDbContext(builder.Options))
                         .LifestyleTransient()
            );

            using var ctx = new DistributedEventBusDbContext(builder.Options);
            ctx.Database.EnsureCreated();
        }

        // Do NOT manually register EfCoreEventOutbox here; it will be added by convention in Initialize of the EFCore module.
        // (EfCoreEventOutbox implements ISingletonDependency.)

        var opts = IocManager.Resolve<DistributedEventBusOptions>();
        opts.Outboxes.Configure("Default", o =>
        {
            o.ImplementationType = typeof(EfCoreEventOutbox);
            o.Selector = _ => true;
            o.IsSendingEnabled = true;
            // Resolve via interface to avoid requiring concrete registration at this phase.
            o.Factory = (resolver, cfg) => resolver.Resolve<IEventOutbox>();
        });

        // Register bus if absent
        if (!IocManager.IsRegistered<IDistributedEventBus>())
            IocManager.Register<IDistributedEventBus, DistributedEventBusBase>(DependencyLifeStyle.Transient);
    }

    public override void Initialize()
    {
        ServiceCollectionRegistrar.Register(IocManager);
    }
}
