using Abp.Modules;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

[DependsOn(typeof(AspNetZeroDistributedEventBusTestBaseModule))]
public class DistributedEventBusTestModule : AbpModule
{

}
