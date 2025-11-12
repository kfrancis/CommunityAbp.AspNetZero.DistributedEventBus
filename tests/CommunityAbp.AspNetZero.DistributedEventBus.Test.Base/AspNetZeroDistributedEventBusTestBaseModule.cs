using Abp.Dependency;
using Abp.Modules;
using Abp.TestBase;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base.DependencyInjection;
using System;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

[DependsOn(
    typeof(AbpTestBaseModule),
    // NOTE: Excluding EF Core inbox/outbox module for now until implementation complete.
    // typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule),
    typeof(AzureDistributedEventServiceBusModule)
)]
public class AspNetZeroDistributedEventBusTestBaseModule : AbpModule
{
    public override void PreInitialize()
    {
        Configuration.BackgroundJobs.IsJobExecutionEnabled = false;
        Configuration.UnitOfWork.Timeout = TimeSpan.FromMinutes(30);
        Configuration.UnitOfWork.IsTransactional = false;

        // Core option singletons
        if (!IocManager.IsRegistered<DistributedEventBusOptions>())
            IocManager.Register<DistributedEventBusOptions>(DependencyLifeStyle.Singleton);
        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
            IocManager.Register<AspNetZeroEventBusBoxesOptions>(DependencyLifeStyle.Singleton);

        // TEMP: Inbox/Outbox (EF) disabled until implementation complete.
        // if (!IocManager.IsRegistered<IEventOutbox>())
        // IocManager.Register<IEventOutbox, EfCoreEventOutbox>(DependencyLifeStyle.Transient);

        if (!IocManager.IsRegistered<IDistributedEventBus>())
            IocManager.Register<IDistributedEventBus, DistributedEventBusBase>(DependencyLifeStyle.Singleton);

        // TEMP: Remove EF Core DbContext setup for inbox/outbox storage.
        // if (!IocManager.IsRegistered<DbContextOptions<DistributedEventBusDbContext>>())
        // {
        // var conn = new SqliteConnection("Data Source=:memory:");
        // conn.Open();
        // var builder = new DbContextOptionsBuilder<DistributedEventBusDbContext>().UseSqlite(conn);
        //
        // IocManager.IocContainer.Register(
        // Component.For<DbContextOptions<DistributedEventBusDbContext>>()
        // .Instance(builder.Options)
        // .LifestyleSingleton(),
        // Component.For<DistributedEventBusDbContext>()
        // .UsingFactoryMethod(k => new DistributedEventBusDbContext(builder.Options))
        // .LifestyleTransient()
        // );
        //
        // using var ctx = new DistributedEventBusDbContext(builder.Options);
        // ctx.Database.EnsureCreated();
        // }

        // TEMP: Do not configure a default EF outbox while feature incomplete.
        // var opts = IocManager.Resolve<DistributedEventBusOptions>();
        // if (opts.Outboxes.Count ==0)
        // {
        // opts.Outboxes.Configure("Default", o =>
        // {
        // o.ImplementationType = typeof(EfCoreEventOutbox);
        // o.Selector = _ => true;
        // o.IsSendingEnabled = true;
        // });
        // }
    }

    public override void Initialize()
    {
        ServiceCollectionRegistrar.Register(IocManager);
    }
}
