using Abp.Events.Bus;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;
using Moq;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

[Trait("Category", "ServiceBusEmulator")]
public class AzureServiceBusEmulatorTests
{
    [Fact]
    public async Task TopicSubscriber_ReceivesDerivedEventThroughBaseHandler()
    {
        var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_EMULATOR_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var registry = new EventTypeRegistry();
        registry.Register<ProgressedEvent>();
        var serializer = new DefaultEventSerializer();
        var options = new AzureServiceBusOptions
        {
            ConnectionString = connectionString,
            EntityPath = "distributed-events",
            SubscriptionName = "mendmd-web",
            EntityKind = AzureServiceBusEntityKind.Topic
        };
        var ioc = Mock.Of<IIocManager>();
        await using var publisher = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), options, ioc, serializer, eventTypes: registry);
        await using var subscriber = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), options, ioc, serializer, eventTypes: registry);
        var received = new TaskCompletionSource<BackgroundJobEventBase>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = subscriber.Subscribe(new BaseHandler(received));

        await publisher.PublishAsync(new ProgressedEvent { JobId = "job-42", Percent = 75 }, DistributedEventDispatchMode.Direct);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(received.Task, completed);
        var value = await received.Task;
        var derived = Assert.IsType<ProgressedEvent>(value);
        Assert.Equal("job-42", derived.JobId);
        Assert.Equal(75, derived.Percent);
    }

    [Fact]
    public async Task QueueConsumer_ReceivesEvents_WhenSubscriptionIsOmitted()
    {
        var connectionString = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_EMULATOR_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var registry = new EventTypeRegistry();
        registry.Register<ProgressedEvent>();
        var options = new AzureServiceBusOptions
        {
            ConnectionString = connectionString,
            EntityPath = "background-jobs",
            EntityKind = AzureServiceBusEntityKind.Queue
        };
        var serializer = new DefaultEventSerializer();
        var ioc = Mock.Of<IIocManager>();
        await using var publisher = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), options, ioc, serializer, eventTypes: registry);
        await using var consumer = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), options, ioc, serializer, eventTypes: registry);
        var received = new TaskCompletionSource<BackgroundJobEventBase>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = consumer.Subscribe(new BaseHandler(received));

        await publisher.PublishAsync(new ProgressedEvent { JobId = "job-queue", Percent = 20 }, DistributedEventDispatchMode.Direct);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(received.Task, completed);
        Assert.Equal("job-queue", (await received.Task).JobId);
    }

    [EventName("mendmd.background-job.progressed")]
    private sealed class ProgressedEvent : BackgroundJobEventBase { public int Percent { get; set; } }
    private class BackgroundJobEventBase : EventData { public string? JobId { get; set; } }
    private sealed class BaseHandler(TaskCompletionSource<BackgroundJobEventBase> received) : IDistributedEventHandler<BackgroundJobEventBase>
    { public Task HandleEventAsync(BackgroundJobEventBase eventData) { received.TrySetResult(eventData); return Task.CompletedTask; } }
}
