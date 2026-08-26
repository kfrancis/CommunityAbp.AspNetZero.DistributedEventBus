using System;
using System.Collections.Generic;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;

/// <summary>Registers explicit handler subscriptions with the ABP module lifecycle.</summary>
public sealed class DistributedEventSubscriptionOptions : ISingletonDependency
{
    internal List<Func<IIocManager, IDistributedEventBus, IDisposable>> Registrations { get; } = [];

    public void Register<TEvent, THandler>()
        where TEvent : class
        where THandler : IDistributedEventHandler<TEvent>
    {
        Registrations.Add((iocManager, bus) => bus.Subscribe(iocManager.Resolve<THandler>()));
    }
}
