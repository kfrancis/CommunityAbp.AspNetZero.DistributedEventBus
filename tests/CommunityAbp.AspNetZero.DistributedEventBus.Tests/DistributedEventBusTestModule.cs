using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using JetBrains.Annotations;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

[UsedImplicitly]
[DependsOn(typeof(AspNetZeroDistributedEventBusTestBaseModule))]
public class DistributedEventBusTestModule : AbpModule
{
    // No need to manually register handler types; global scan in core module handles auto-registration.
}
