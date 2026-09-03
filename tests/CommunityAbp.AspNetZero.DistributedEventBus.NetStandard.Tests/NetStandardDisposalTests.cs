using System.Reflection;
using System.Runtime.Versioning;
using Abp.Dependency;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;
using Moq;

namespace CommunityAbp.AspNetZero.DistributedEventBus.NetStandard.Tests;

/// <summary>Runs the netstandard2.0 build of the Azure bus, where the synchronous Dispose() is the only release path.</summary>
public class NetStandardDisposalTests
{
    private static AzureServiceBusDistributedEventBus CreateBus() => new(
        new DistributedEventBusOptions(),
        new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=Root;SharedAccessKey=key",
            EntityPath = "distributed-events",
            SubscriptionName = "mendmd-web",
            EntityKind = AzureServiceBusEntityKind.Topic
        },
        Mock.Of<IIocManager>(),
        new DefaultEventSerializer());

    private static T GetPrivate<T>(object target, string field) =>
        (T)typeof(AzureServiceBusDistributedEventBus).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    [Fact]
    public void AssemblyUnderTest_IsTheNetStandard20Build_WithoutIAsyncDisposable()
    {
        var assembly = typeof(AzureServiceBusDistributedEventBus).Assembly;
        var framework = assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        Assert.Equal(".NETStandard,Version=v2.0", framework);
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(typeof(AzureServiceBusDistributedEventBus)));
    }

    [Fact]
    public void Dispose_ClosesClientAndSender_AndSecondDisposeIsNoOp()
    {
        var bus = CreateBus();
        var client = GetPrivate<ServiceBusClient>(bus, "_client");
        var sender = GetPrivate<ServiceBusSender>(bus, "_sender");
        Assert.False(client.IsClosed);

        bus.Dispose();

        Assert.True(client.IsClosed);
        Assert.True(sender.IsClosed);
        bus.Dispose();
        Assert.True(client.IsClosed);
    }

    [Fact]
    public async Task PublishAndSubscribe_AfterDispose_ThrowObjectDisposedException()
    {
        var bus = CreateBus();
        bus.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => bus.PublishAsync(new Payload(), DistributedEventDispatchMode.Direct));
        Assert.Throws<ObjectDisposedException>(() => bus.Subscribe(new PayloadHandler()));
    }

    private sealed class Payload;

    private sealed class PayloadHandler : IDistributedEventHandler<Payload>
    {
        public Task HandleEventAsync(Payload eventData) => Task.CompletedTask;
    }
}
