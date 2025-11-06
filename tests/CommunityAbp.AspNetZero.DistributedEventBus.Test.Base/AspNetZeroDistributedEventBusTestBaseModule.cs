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
        efModule.SkipDbContextRegistration = true;
    }

    public override void PreInitialize()
    {
        Configuration.BackgroundJobs.IsJobExecutionEnabled = false;
        Configuration.UnitOfWork.Timeout = TimeSpan.FromMinutes(30);
        Configuration.UnitOfWork.IsTransactional = false;

        // Use existing registration or create singleton if missing
        if (!IocManager.IsRegistered<DistributedEventBusOptions>())
            IocManager.Register<DistributedEventBusOptions>(DependencyLifeStyle.Singleton);
        var opts = IocManager.Resolve<DistributedEventBusOptions>();

        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
            IocManager.Register<AspNetZeroEventBusBoxesOptions>(DependencyLifeStyle.Singleton);


        if (!IocManager.IsRegistered<IEventOutbox>())
            IocManager.Register<IEventOutbox, EfCoreEventOutbox>(DependencyLifeStyle.Transient);

        if (!IocManager.IsRegistered<IDistributedEventBus>())
            IocManager.Register<IDistributedEventBus, DistributedEventBusBase>(DependencyLifeStyle.Singleton);

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

        // Only configure outbox if not already configured to avoid duplicates
        if (opts.Outboxes.Count == 0)
        {
            opts.Outboxes.Configure("Default", o =>
            {
                o.ImplementationType = typeof(EfCoreEventOutbox);
                o.Selector = _ => true;
                o.IsSendingEnabled = true; // enable persistence but we won't start sender in this test
            });
        }
    }

    public override void Initialize()
    {
        ServiceCollectionRegistrar.Register(IocManager);
    }
}
