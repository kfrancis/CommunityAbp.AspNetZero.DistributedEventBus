using System;
using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;
using System.Linq;
using Abp.Events.Bus.Handlers;
using System.Reflection;

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
    }

    public override void PostInitialize()
    {
        // After all modules initialized, scan all loaded assemblies for distributed handlers.
        var options = IocManager.Resolve<DistributedEventBusOptions>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || t.IsInterface) continue;
                // Must implement at least one IDistributedEventHandler<> generic interface
                var distributedInterfaces = t.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDistributedEventHandler<>)).ToArray();
                if (distributedInterfaces.Length == 0) continue;

                // Register handler type if not already registered in IoC.
                // Use Singleton because current bus implementation captures the handler instance at subscription time;
                // using transient would capture an arbitrary single instance and confuse expectations (especially in tests).
                if (!IocManager.IsRegistered(t))
                {
                    IocManager.Register(t, DependencyLifeStyle.Singleton);
                }

                // Add to options.Handlers for auto subscription when bus constructs
                if (!options.Handlers.Contains(t))
                {
                    options.Handlers.Add(t);
                }
            }
        }
        // Removed eager subscription initialization to allow tests to register late dependencies (e.g. SignalR hub context) before handler resolution.
    }
}
