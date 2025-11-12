using System.Text.RegularExpressions;
using Abp;
using Abp.Events.Bus;
using Abp.Events.Bus.Entities;
using Abp.Modules;
using Abp.TestBase;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Test.Base
{
    /// <summary>
    ///     This is base class for all our test classes.
    ///     It prepares ABP system, modules and a fake, in-memory database.
    ///     Seeds database with initial data.
    ///     Provides methods to easily work with <see cref="DistributedEventBusDbContext" />.
    /// </summary>
    public abstract class AppTestBase<T> : AbpIntegratedTestBase<T> where T : AbpModule
    {
        private const string DefaultStringChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        protected AppTestBase()
        {
            // No need to register DbContextOptions here; handled in module
            SeedTestData();
        }

        protected string GetRandomString(int maxLength = 13, int minLength = 0, string regexPattern = "[^A-Za-z0-9]")
        {
            if (minLength < 0 || maxLength < 0 || minLength > maxLength)
            {
                throw new AbpException("Invalid minLength or maxLength parameters");
            }

            var random = new Random();
            var regex = new Regex(regexPattern);

            var length = random.Next(minLength, maxLength + 1);

            var filteredChars = new string(DefaultStringChars.Where(c => !regex.IsMatch(c.ToString())).ToArray());

            return new string(Enumerable.Repeat(filteredChars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        protected void UsingDbContext(Action<DistributedEventBusDbContext> action)
        {
            UsingDbContext(AbpSession.TenantId, action);
        }

        protected TResult UsingDbContext<TResult>(Func<DistributedEventBusDbContext, TResult> func)
        {
            return UsingDbContext(AbpSession.TenantId, func);
        }

        private void UsingDbContext(int? tenantId, Action<DistributedEventBusDbContext> action)
        {
            using (UsingTenantId(tenantId))
            {
                using (var context = LocalIocManager.Resolve<DistributedEventBusDbContext>())
                {
                    action(context);
                    context.SaveChanges();
                }
            }
        }

        private TResult UsingDbContext<TResult>(int? tenantId, Func<DistributedEventBusDbContext, TResult> func)
        {
            TResult result;

            using (UsingTenantId(tenantId))
            {
                using (var context = LocalIocManager.Resolve<DistributedEventBusDbContext>())
                {
                    result = func(context);
                    context.SaveChanges();
                }
            }

            return result;
        }

        protected Task UsingDbContextAsync(Func<DistributedEventBusDbContext, Task> action)
        {
            return UsingDbContextAsync(AbpSession.TenantId, action);
        }

        protected Task<TResult> UsingDbContextAsync<TResult>(Func<DistributedEventBusDbContext, Task<TResult>> func)
        {
            return UsingDbContextAsync(AbpSession.TenantId, func);
        }

        private async Task UsingDbContextAsync(int? tenantId, Func<DistributedEventBusDbContext, Task> action)
        {
            using (UsingTenantId(tenantId))
            {
                await using (var context = LocalIocManager.Resolve<DistributedEventBusDbContext>())
                {
                    await action(context);
                    await context.SaveChangesAsync();
                }
            }
        }

        private async Task<TResult> UsingDbContextAsync<TResult>(int? tenantId,
            Func<DistributedEventBusDbContext, Task<TResult>> func)
        {
            TResult result;

            using (UsingTenantId(tenantId))
            {
                await using (var context = LocalIocManager.Resolve<DistributedEventBusDbContext>())
                {
                    result = await func(context);
                    await context.SaveChangesAsync();
                }
            }

            return result;
        }

        private IDisposable UsingTenantId(int? tenantId)
        {
            var previousTenantId = AbpSession.TenantId;
            AbpSession.TenantId = tenantId;
            return new DisposeAction(() => AbpSession.TenantId = previousTenantId);
        }

        /// <summary>
        ///     Replaces a service in the IoC container with the given instance.
        /// </summary>
        protected void ReplaceService<TService>(TService instance)
            where TService : class
        {
            LocalIocManager.IocContainer.Register(
                Component.For<TService>().Instance(instance).IsDefault()
            );
        }

        private void SeedTestData()
        {
            //Seed initial data for default tenant
            AbpSession.TenantId = 1;

            UsingDbContext(context =>
            {
                NormalizeDbContext(context);
                new TestDataBuilder(context, 1).Create();
            });
            return;

            void NormalizeDbContext(DistributedEventBusDbContext context)
            {
                context.EntityChangeEventHelper = NullEntityChangeEventHelper.Instance;
                context.EventBus = NullEventBus.Instance;
                context.SuppressAutoSetTenantId = true;
            }
        }
    }

    public class TestDataBuilder
    {
        private readonly DistributedEventBusDbContext _context;
        private readonly int _tenantId;

        public TestDataBuilder(DistributedEventBusDbContext context, int tenantId)
        {
            _context = context;
            _tenantId = tenantId;
        }

        public void Create()
        {
            _context.SaveChanges();
        }
    }
}
