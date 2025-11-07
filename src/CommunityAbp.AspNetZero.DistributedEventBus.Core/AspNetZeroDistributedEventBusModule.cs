using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;
using System.Linq;
using Abp.Events.Bus.Handlers;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public class AspNetZeroDistributedEventBusModule : AbpModule
{
    public override void PreInitialize()
    {
        // Ensure options live as singletons so runtime configuration (tests) is visible to bus instances.
        if (!IocManager.IsRegistered<DistributedEventBusOptions>())
        {
            IocManager.Register<DistributedEventBusOptions>(DependencyLifeStyle.Singleton);
        }
        if (!IocManager.IsRegistered<AspNetZeroEventBusBoxesOptions>())
        {
            IocManager.Register<AspNetZeroEventBusBoxesOptions>(DependencyLifeStyle.Singleton);
        }
        // Register serializer singleton
        if (!IocManager.IsRegistered<IEventSerializer>())
        {
            IocManager.Register<IEventSerializer, DefaultEventSerializer>(DependencyLifeStyle.Singleton);
        }
        IocManager.Register<IOutboxSender, PollingOutboxSender>(DependencyLifeStyle.Singleton);
        IocManager.Register<IInboxProcessor, PollingInboxProcessor>(DependencyLifeStyle.Singleton);
    }

    public override void Initialize()
    {
        IocManager.RegisterAssemblyByConvention(typeof(AspNetZeroDistributedEventBusModule).Assembly);

        // Populate handlers list for auto subscription (similar to ABP behavior)
        var options = IocManager.Resolve<DistributedEventBusOptions>();
        var handlerTypes = typeof(AspNetZeroDistributedEventBusModule).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IEventHandler).IsAssignableFrom(t))
            .ToList();
        foreach (var ht in handlerTypes)
        {
            if (!options.Handlers.Contains(ht))
            {
                options.Handlers.Add(ht);
            }
        }
    }
}
