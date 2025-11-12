using System;
using Abp.EntityFrameworkCore;
using Abp.EntityFrameworkCore.Configuration;
using Abp.Modules;
using Abp.Reflection.Extensions;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using System.Diagnostics.CodeAnalysis;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore
{
    [DependsOn(
        typeof(AspNetZeroDistributedEventBusModule),
        typeof(AbpEntityFrameworkCoreModule)
        )]
    [Obsolete("EF Core inbox/outbox module is incomplete / experimental. Do not use for production durability.")]
    public class AspNetZeroDistributedEventEntityFrameworkCoreModule : AbpModule
    {
        /* Used it tests to skip DbContext registration, in order to use in-memory database of EF Core */
        public bool SkipDbContextRegistration { get; set; }

        public override void PreInitialize()
        {
            // Emit a runtime warning to make clear EF persistence is incomplete.
            Console.WriteLine("[DistributedEventBus WARNING] EF Core Inbox/Outbox module is INCOMPLETE / EXPERIMENTAL. Do not rely on for production durability.");

            if (!SkipDbContextRegistration)
            {
                Configuration.Modules.AbpEfCore().AddDbContext<DistributedEventBusDbContext>(options =>
                {
                    if (options.ExistingConnection != null)
                    {
                        DbContextOptionsConfigurer.Configure(options.DbContextOptions, options.ExistingConnection);
                    }
                    else
                    {
                        DbContextOptionsConfigurer.Configure(options.DbContextOptions, options.ConnectionString);
                    }
                });
            }
        }

        public override void Initialize()
        {
            IocManager.RegisterAssemblyByConvention(typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule).GetAssembly());
        }
    }
}
