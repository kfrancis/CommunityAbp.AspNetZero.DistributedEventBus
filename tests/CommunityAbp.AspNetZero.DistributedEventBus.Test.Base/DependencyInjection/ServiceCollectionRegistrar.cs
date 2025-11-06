using Abp.Dependency;
using Castle.MicroKernel.Registration;
using Castle.Windsor.MsDependencyInjection;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Debug;
using Microsoft.Extensions.Logging.Console;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base.DependencyInjection;

public static class ServiceCollectionRegistrar
{
    public static void Register(IIocManager iocManager)
    {
        RegisterIdentity(iocManager);

        var builder = new DbContextOptionsBuilder<DistributedEventBusDbContext>();
        var inMemorySqlite = new SqliteConnection("Data Source=:memory:");
        builder.UseSqlite(inMemorySqlite);

        iocManager.IocContainer.Register(
            Component.For<DbContextOptions<DistributedEventBusDbContext>>()
                     .Instance(builder.Options)
                     .LifestyleSingleton()
        );

        inMemorySqlite.Open();
        new DistributedEventBusDbContext(builder.Options).Database.EnsureCreated();
    }

    private static void RegisterIdentity(IIocManager iocManager)
    {
        var services = new ServiceCollection();

        services.AddLogging(lb => lb.AddDebug().AddConsole()); // <-- add this

        WindsorRegistrationHelper.CreateServiceProvider(iocManager.IocContainer, services);
    }
}
// (No additional registrations required for AspNetZero 14.3.0 at this time.)
