using System.Diagnostics;
using System.Reflection;
using Abp.Dependency;
using Abp.Events.Bus;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Serialization;
using Microsoft.Extensions.Logging;
using Moq;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

/// <summary>
///     Disposal and dispatch-semantics tests that need no broker. The SDK client is created but never connected, so
///     <c>Dispose</c>/<c>DisposeAsync</c> exercise the real shutdown path against unopened links.
/// </summary>
public class AzureServiceBusDisposalTests
{
    private const string ConnectionString = "Endpoint=sb://localhost;SharedAccessKeyName=Root;SharedAccessKey=key";

    private static AzureServiceBusOptions TopicOptions => new()
    {
        ConnectionString = ConnectionString,
        EntityPath = "distributed-events",
        SubscriptionName = "mendmd-web",
        EntityKind = AzureServiceBusEntityKind.Topic
    };

    private static AzureServiceBusDistributedEventBus CreateBus(ILogger<AzureServiceBusDistributedEventBus>? logger = null, IEventTypeRegistry? registry = null) =>
        new(new DistributedEventBusOptions(), TopicOptions, Mock.Of<IIocManager>(), new DefaultEventSerializer(), eventTypes: registry, logger: logger);

    private static T GetPrivate<T>(object target, string field) =>
        (T)typeof(AzureServiceBusDistributedEventBus).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static void SetPrivate(object target, string field, object value) =>
        typeof(AzureServiceBusDistributedEventBus).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

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
        bus.Dispose(); // idempotent
        Assert.True(client.IsClosed);
    }

    [Fact]
    public async Task DisposeAsync_ClosesClientAndSender_AndSecondDisposeAsyncIsNoOp()
    {
        var bus = CreateBus();
        var client = GetPrivate<ServiceBusClient>(bus, "_client");
        var sender = GetPrivate<ServiceBusSender>(bus, "_sender");

        await bus.DisposeAsync();

        Assert.True(client.IsClosed);
        Assert.True(sender.IsClosed);
        await bus.DisposeAsync(); // idempotent
        Assert.True(client.IsClosed);
    }

    [Fact]
    public async Task Dispose_ThenDisposeAsync_AndViceVersa_AreMutuallySafe()
    {
        var first = CreateBus();
        first.Dispose();
        await first.DisposeAsync();
        Assert.True(GetPrivate<ServiceBusClient>(first, "_client").IsClosed);

        var second = CreateBus();
        await second.DisposeAsync();
        second.Dispose();
        Assert.True(GetPrivate<ServiceBusClient>(second, "_client").IsClosed);
    }

    [Fact]
    public async Task ConcurrentDispose_RunsShutdownOnce()
    {
        var logger = new ListLogger();
        var bus = CreateBus(logger);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(bus.Dispose)));

        Assert.True(GetPrivate<ServiceBusClient>(bus, "_client").IsClosed);
        Assert.Equal(1, logger.Count("Disposed ServiceBusClient"));
    }

    [Fact]
    public async Task PublishAndSubscribe_AfterDispose_ThrowObjectDisposedException()
    {
        var bus = CreateBus();
        bus.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => bus.PublishAsync(new DisposalEvent(), DistributedEventDispatchMode.Direct));
        Assert.Throws<ObjectDisposedException>(() => bus.Subscribe(new NoopHandler()));
    }

    [Fact]
    public void Lifecycle_LogsOneClientCreationAndOneDisposal()
    {
        var logger = new ListLogger();
        var bus = CreateBus(logger);
        Assert.Equal(1, logger.Count("Created ServiceBusClient"));

        bus.Dispose();
        bus.Dispose();

        Assert.Equal(1, logger.Count("Created ServiceBusClient"));
        Assert.Equal(1, logger.Count("Disposed ServiceBusSender"));
        Assert.Equal(1, logger.Count("Disposed ServiceBusClient"));
        Assert.All(logger.Entries.Where(e => e.Message.Contains("ServiceBusClient")), e => Assert.Equal(LogLevel.Debug, e.Level));
    }

    [Fact]
    public void AzureBus_NeverDispatchesLocallyOnDirectPublish()
    {
        using var probe = new ProbeBus();
        Assert.False(probe.LocalDispatch);
    }

    [Fact]
    public async Task AzureBus_DirectPublish_SendsOnce_AndTagsLocalDispatchFalse()
    {
        var registry = new EventTypeRegistry();
        registry.Register<DisposalEvent>();
        var sender = new Mock<ServiceBusSender>();
        var bus = CreateBus(registry: registry);
        SetPrivate(bus, "_sender", sender.Object);
        using var listener = new PublishActivityListener();

        await bus.PublishAsync(new DisposalEvent(), DistributedEventDispatchMode.Direct);

        sender.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        var publish = listener.Find("tests.disposal-event");
        Assert.NotNull(publish);
        Assert.Equal(false, publish.GetTagItem("distributed_eventbus.local_dispatch"));
        bus.Dispose();
    }

    [Fact]
    public async Task BaseBus_DirectPublish_DispatchesLocallyOnce_AndTagsLocalDispatchTrue()
    {
        var registry = new EventTypeRegistry();
        registry.Register<BaseBusEvent>();
        using var bus = new DistributedEventBusBase(new DistributedEventBusOptions(), Mock.Of<IIocManager>(), new DefaultEventSerializer(), registry);
        var count = 0;
        using var subscription = bus.Subscribe(new CountingHandler(() => count++));
        using var listener = new PublishActivityListener();

        await bus.PublishAsync(new BaseBusEvent(), DistributedEventDispatchMode.Direct);

        Assert.Equal(1, count);
        var publish = listener.Find("tests.base-bus-event");
        Assert.NotNull(publish);
        Assert.Equal(true, publish.GetTagItem("distributed_eventbus.local_dispatch"));
    }

    [EventName("tests.disposal-event")]
    private sealed class DisposalEvent : EventData;

    [EventName("tests.base-bus-event")]
    private sealed class BaseBusEvent : EventData;

    private sealed class NoopHandler : IDistributedEventHandler<DisposalEvent>
    {
        public Task HandleEventAsync(DisposalEvent eventData) => Task.CompletedTask;
    }

    private sealed class CountingHandler(Action onHandle) : IDistributedEventHandler<BaseBusEvent>
    {
        public Task HandleEventAsync(BaseBusEvent eventData) { onHandle(); return Task.CompletedTask; }
    }

    private sealed class ProbeBus() : AzureServiceBusDistributedEventBus(new DistributedEventBusOptions(), TopicOptions, Mock.Of<IIocManager>(), new DefaultEventSerializer())
    {
        public bool LocalDispatch => DispatchLocallyOnDirectPublish;
    }

    /// <summary>Captures completed publish activities so tag assertions can target a single event name.</summary>
    private sealed class PublishActivityListener : IDisposable
    {
        private readonly List<Activity> _activities = [];
        private readonly ActivityListener _listener;

        public PublishActivityListener()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == DistributedEventBusDiagnostics.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity => { lock (_activities) _activities.Add(activity); }
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public Activity? Find(string eventName)
        {
            lock (_activities)
                return _activities.FirstOrDefault(a => a.OperationName == "distributed-eventbus.publish" && Equals(a.GetTagItem("messaging.message.type"), eventName));
        }

        public void Dispose() => _listener.Dispose();
    }

    internal sealed class ListLogger : ILogger<AzureServiceBusDistributedEventBus>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public int Count(string fragment) { lock (Entries) return Entries.Count(e => e.Message.Contains(fragment, StringComparison.Ordinal)); }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
