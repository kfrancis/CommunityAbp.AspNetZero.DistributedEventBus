using Abp.Modules;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base; // Add this for InMemoryDistributedEventBus
using NSubstitute;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base
{
    [DependsOn(
        typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule)
    )]
    public class DistributedEventBusTestBaseModule : AbpModule
    {
        public override void PreInitialize()
        {
            // Register in-memory DbContextOptions for testing
            var builder = new DbContextOptionsBuilder<DistributedEventBusDbContext>();
            builder.UseInMemoryDatabase("TestDb");
            var options = builder.Options;

            // Register generic DbContextOptions<DistributedEventBusDbContext>
            IocManager.IocContainer.Register(
                Component.For<DbContextOptions<DistributedEventBusDbContext>>()
                    .Instance(options)
                    .LifestyleSingleton()
            );

            // Register non-generic DbContextOptions for base class constructor
            IocManager.IocContainer.Register(
                Component.For<DbContextOptions>()
                    .Instance(options)
                    .LifestyleSingleton()
            );

            // Register a concrete, non-abstract implementation for IDistributedEventBus
            IocManager.IocContainer.Register(
                Component.For<IDistributedEventBus>()
                    .ImplementedBy<InMemoryDistributedEventBus>() // Use the in-memory test implementation
                    .LifestyleSingleton()
            );

            // Register a mock for ISupportsEventBoxes to allow replacement in tests
            var mockOutboxManager = Substitute.For<ISupportsEventBoxes>();
            IocManager.IocContainer.Register(
                Component.For<ISupportsEventBoxes>()
                    .Instance(mockOutboxManager)
                    .LifestyleSingleton()
            );

            // Register any other required dependencies or mocks here
        }

        public override void Initialize()
        {
            IocManager.RegisterAssemblyByConvention(typeof(DistributedEventBusTestBaseModule).Assembly);
        }
    }
}
