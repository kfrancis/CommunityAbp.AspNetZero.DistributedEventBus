using Abp.Dependency;
using Castle.MicroKernel.Registration;
using Castle.Windsor.MsDependencyInjection;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base.DependencyInjection;

public static class ServiceCollectionRegistrar
{
    public static void Register(IIocManager iocManager)
    {
        RegisterIdentity(iocManager);

        // DbContext & options now registered in test module PreInitialize. Avoid duplicate registration.
        // Keep schema ensure only if not already created.
        if (!iocManager.IsRegistered<DbContextOptions<DistributedEventBusDbContext>>())
        {
            var builder = new DbContextOptionsBuilder<DistributedEventBusDbContext>();
            var inMemorySqlite = new SqliteConnection("Data Source=:memory:");
            builder.UseSqlite(inMemorySqlite);

            iocManager.IocContainer.Register(
                Component.For<DbContextOptions<DistributedEventBusDbContext>>()
                         .Instance(builder.Options)
                         .LifestyleSingleton(),
                Component.For<DistributedEventBusDbContext>()
                         .UsingFactoryMethod(kernel => new DistributedEventBusDbContext(kernel.Resolve<DbContextOptions<DistributedEventBusDbContext>>()))
                         .LifestyleTransient()
            );

            inMemorySqlite.Open();
            new DistributedEventBusDbContext(builder.Options).Database.EnsureCreated();
        }

        // Concrete outbox type registration guard (may already be registered singleton elsewhere)
        if (!iocManager.IsRegistered<EfCoreEventOutbox>())
        {
            iocManager.IocContainer.Register(
                Component.For<EfCoreEventOutbox>()
                         .ImplementedBy<EfCoreEventOutbox>()
                         .LifestyleSingleton()
            );
        }
    }

    private static void RegisterIdentity(IIocManager iocManager)
    {
        var services = new ServiceCollection();
        services.AddLogging(lb => lb.AddDebug().AddConsole());
        WindsorRegistrationHelper.CreateServiceProvider(iocManager.IocContainer, services);
    }
}
