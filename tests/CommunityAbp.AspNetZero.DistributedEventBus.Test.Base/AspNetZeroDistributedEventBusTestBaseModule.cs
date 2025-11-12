using Abp.Dependency;
using Abp.Modules;
using Abp.TestBase;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

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
        {
            IocManager.Register<DistributedEventBusOptions>();
        }

        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
        {
            IocManager.Register<AspNetZeroEventBusBoxesOptions>();
        }

        if (!IocManager.IsRegistered<IDistributedEventBus>())
        {
            IocManager.Register<IDistributedEventBus, DistributedEventBusBase>();
        }

        // Setup in-memory SQLite database for outbox/inbox testing
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

        // Register EF Core outbox implementation
        if (!IocManager.IsRegistered<IEventOutbox>())
        {
            IocManager.Register<IEventOutbox, EfCoreEventOutbox>(DependencyLifeStyle.Transient);
        }

        // Configure default outbox for tests
        var opts = IocManager.Resolve<DistributedEventBusOptions>();
        if (opts.Outboxes.Count == 0)
        {
            opts.Outboxes.Configure("Default", o =>
            {
                o.ImplementationType = typeof(EfCoreEventOutbox);
                o.Selector = _ => true;
                o.IsSendingEnabled = true;
            });
        }
    }

    public override void Initialize()
    {
        ServiceCollectionRegistrar.Register(IocManager);
    }
}
