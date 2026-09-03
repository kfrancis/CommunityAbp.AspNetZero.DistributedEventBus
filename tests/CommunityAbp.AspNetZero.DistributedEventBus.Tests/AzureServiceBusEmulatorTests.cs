using System.Reflection;
using Abp.Events.Bus;
using Abp.Dependency;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;
using Moq;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

/// <summary>
///     Runs against the Azure Service Bus emulator (see .github/servicebus-emulator). Every test returns early when
///     AZURE_SERVICEBUS_EMULATOR_CONNECTION is not set.
/// </summary>
[Trait("Category", "ServiceBusEmulator")]
public class AzureServiceBusEmulatorTests
{
    private static string? EmulatorConnectionString => Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_EMULATOR_CONNECTION");

    private static AzureServiceBusOptions TopicOptions(string connectionString) => new()
    {
        ConnectionString = connectionString,
        EntityPath = "distributed-events",
        SubscriptionName = "mendmd-web",
        EntityKind = AzureServiceBusEntityKind.Topic
    };

    [Fact]
    public async Task TopicSubscriber_ReceivesDerivedEventThroughBaseHandler()
    {
        var connectionString = EmulatorConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var registry = new EventTypeRegistry();
        registry.Register<ProgressedEvent>();
        var serializer = new DefaultEventSerializer();
        var options = TopicOptions(connectionString);
        var ioc = Mock.Of<IIocManager>();
        await using var publisher = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), options, ioc, serializer, eventTypes: registry);
        await using var subscriber = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), options, ioc, serializer, eventTypes: registry);
        var jobId = UniqueJobId("job-42");
        var received = new TaskCompletionSource<BackgroundJobEventBase>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = subscriber.Subscribe(new BaseHandler(received, jobId));

        await publisher.PublishAsync(new ProgressedEvent { JobId = jobId, Percent = 75 }, DistributedEventDispatchMode.Direct);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(received.Task, completed);
        var value = await received.Task;
        var derived = Assert.IsType<ProgressedEvent>(value);
        Assert.Equal(jobId, derived.JobId);
        Assert.Equal(75, derived.Percent);
    }

    [Fact]
    public async Task QueueConsumer_ReceivesEvents_WhenSubscriptionIsOmitted()
    {
        var connectionString = EmulatorConnectionString;
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
        var jobId = UniqueJobId("job-queue");
        var received = new TaskCompletionSource<BackgroundJobEventBase>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = consumer.Subscribe(new BaseHandler(received, jobId));

        await publisher.PublishAsync(new ProgressedEvent { JobId = jobId, Percent = 20 }, DistributedEventDispatchMode.Direct);

        var completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(received.Task, completed);
        Assert.Equal(jobId, (await received.Task).JobId);
    }

    /// <summary>
    ///     The MVC host case: one singleton bus publishes to and consumes from the same topic subscription.
    ///     The handler must run exactly once, from the broker copy, with broker metadata in the context accessor.
    /// </summary>
    [Fact]
    public async Task SingletonBus_PublishAndSubscribeOnSameEntity_InvokesHandlerOnce_WithBrokerMetadata()
    {
        var connectionString = EmulatorConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var registry = new EventTypeRegistry();
        registry.Register<ProgressedEvent>();
        var accessor = new DistributedEventContextAccessor();
        var bus = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), TopicOptions(connectionString), Mock.Of<IIocManager>(),
            new DefaultEventSerializer(), eventTypes: registry, contextAccessor: accessor);
        var jobId = UniqueJobId("job-singleton");
        var handler = new CountingHandler(accessor, jobId);
        using var subscription = bus.Subscribe(handler);
        Assert.True(bus.HasActiveProcessor);

        await bus.PublishAsync(new ProgressedEvent { JobId = jobId, Percent = 99 }, DistributedEventDispatchMode.Direct);

        var completed = await Task.WhenAny(handler.FirstInvocation, Task.Delay(TimeSpan.FromSeconds(20)));
        Assert.Same(handler.FirstInvocation, completed);
        await Task.Delay(TimeSpan.FromSeconds(3)); // grace period: a second (in-process echo) invocation would land here
        Assert.True(handler.Count == 1, $"Expected exactly one invocation for {jobId} but got {handler.Count}. Deliveries: {handler.Describe()}");

        var context = Assert.IsType<DistributedEventMessageContext>(handler.Context);
        Assert.Equal("distributed-events", context.EntityPath);
        Assert.Equal("mendmd-web", context.SubscriptionName);
        Assert.False(string.IsNullOrWhiteSpace(context.MessageId));
        Assert.True(context.DeliveryCount >= 1);
        Assert.Equal("mendmd.background-job.progressed", context.EventName);
        Assert.Equal(jobId, Assert.IsType<ProgressedEvent>(handler.LastEvent).JobId);

        await bus.DisposeAsync();
        Assert.False(bus.HasActiveProcessor);
        Assert.True(GetClient(bus).IsClosed);
    }

    /// <summary>The synchronous Dispose path (Castle release / netstandard2.0) must stop a live processor and close the client.</summary>
    [Fact]
    public async Task SingletonBus_SyncDispose_StopsProcessorAndClosesClient()
    {
        var connectionString = EmulatorConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var registry = new EventTypeRegistry();
        registry.Register<ProgressedEvent>();
        var bus = new AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), TopicOptions(connectionString), Mock.Of<IIocManager>(),
            new DefaultEventSerializer(), eventTypes: registry);
        var handler = new CountingHandler(new DistributedEventContextAccessor(), UniqueJobId("job-sync-dispose"));
        using var subscription = bus.Subscribe(handler);
        var processor = GetProcessor(bus);
        Assert.NotNull(processor);
        Assert.True(processor.IsProcessing);

        bus.Dispose();

        Assert.False(bus.HasActiveProcessor);
        Assert.False(processor.IsProcessing);
        Assert.True(processor.IsClosed);
        Assert.True(GetClient(bus).IsClosed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => bus.PublishAsync(new ProgressedEvent(), DistributedEventDispatchMode.Direct));
    }

    private static ServiceBusClient GetClient(AzureServiceBusDistributedEventBus bus) =>
        (ServiceBusClient)typeof(AzureServiceBusDistributedEventBus).GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus)!;

    private static ServiceBusProcessor? GetProcessor(AzureServiceBusDistributedEventBus bus) =>
        (ServiceBusProcessor?)typeof(AzureServiceBusDistributedEventBus).GetField("_processor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bus);

    // The emulator entities are shared by every test (and every run), so a message abandoned during a previous test's
    // shutdown can be redelivered to a later test. Handlers therefore key on a per-test JobId; an in-process echo of the
    // same publish would carry the same JobId and still be counted.
    private static string UniqueJobId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    [EventName("mendmd.background-job.progressed")]
    private sealed class ProgressedEvent : BackgroundJobEventBase { public int Percent { get; set; } }
    private class BackgroundJobEventBase : EventData { public string? JobId { get; set; } }
    private sealed class BaseHandler(TaskCompletionSource<BackgroundJobEventBase> received, string expectedJobId) : IDistributedEventHandler<BackgroundJobEventBase>
    {
        public Task HandleEventAsync(BackgroundJobEventBase eventData)
        {
            if (eventData.JobId == expectedJobId) received.TrySetResult(eventData);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingHandler(IDistributedEventContextAccessor accessor, string expectedJobId) : IDistributedEventHandler<BackgroundJobEventBase>
    {
        private readonly TaskCompletionSource<bool> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<string> _deliveries = [];
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public Task FirstInvocation => _first.Task;
        public BackgroundJobEventBase? LastEvent { get; private set; }
        public DistributedEventMessageContext? Context { get; private set; }
        public string Describe() { lock (_deliveries) return string.Join("; ", _deliveries); }
        public Task HandleEventAsync(BackgroundJobEventBase eventData)
        {
            var context = accessor.Current;
            lock (_deliveries) _deliveries.Add($"{eventData.JobId} (message {context?.MessageId}, delivery {context?.DeliveryCount})");
            if (eventData.JobId != expectedJobId) return Task.CompletedTask;
            LastEvent = eventData;
            Context = context;
            Interlocked.Increment(ref _count);
            _first.TrySetResult(true);
            return Task.CompletedTask;
        }
    }
}
