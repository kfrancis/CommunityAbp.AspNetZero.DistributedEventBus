using Abp.Events.Bus;
using Abp;
using Abp.Dependency;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class ProductionProfileTests : AppTestBase<DistributedEventBusTestModule>
{
    [Fact]
    public async Task DirectMode_DeliversDerivedEventOnce_AndExposesMetadata()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handler = new BaseEventHandler(Resolve<IDistributedEventContextAccessor>());
        var first = bus.Subscribe(handler);
        var duplicate = bus.Subscribe(handler);

        await bus.PublishAsync(new DerivedProgressEvent { JobId = "job-1", Percent = 50 }, DistributedEventDispatchMode.Direct);

        Assert.Equal(1, handler.Count);
        Assert.IsType<DerivedProgressEvent>(handler.LastEvent);
        Assert.Equal("jobs.progressed", handler.Context?.EventName);
        Assert.False(string.IsNullOrWhiteSpace(handler.Context?.MessageId));
        duplicate.Dispose();
        await bus.PublishAsync(new DerivedProgressEvent(), DistributedEventDispatchMode.Direct);
        Assert.Equal(1, handler.Count);
        first.Dispose();
    }

    [Fact]
    public void EventTypeRegistry_ResolvesStableAndLegacyAliases()
    {
        var registry = Resolve<IEventTypeRegistry>();
        registry.Register<DerivedProgressEvent>("legacy.jobs.progressed");

        Assert.Equal("jobs.progressed", registry.GetEventName(typeof(DerivedProgressEvent)));
        Assert.Equal(typeof(DerivedProgressEvent), registry.Resolve("jobs.progressed"));
        Assert.Equal(typeof(DerivedProgressEvent), registry.Resolve("legacy.jobs.progressed"));
        Assert.Equal(typeof(DerivedProgressEvent), registry.Resolve(typeof(DerivedProgressEvent).AssemblyQualifiedName!));
    }

    [Fact]
    public async Task CancelledPublish_DoesNotInvokeHandlers()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handler = new BaseEventHandler(Resolve<IDistributedEventContextAccessor>());
        bus.Subscribe(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            bus.PublishAsync(new DerivedProgressEvent(), DistributedEventDispatchMode.Direct, cancellationToken: cts.Token));
        Assert.Equal(0, handler.Count);
    }

    [Fact]
    public async Task InboxProcessing_PreservesBrokerMessageContext()
    {
        var registry = Resolve<IEventTypeRegistry>();
        registry.Register<DerivedProgressEvent>();
        var serializer = Resolve<IEventSerializer>();
        var bus = Resolve<IDistributedEventBus>();
        var handler = new BaseEventHandler(Resolve<IDistributedEventContextAccessor>());
        bus.Subscribe(handler);
        var messageContext = new DistributedEventMessageContext
        {
            MessageId = "broker-message-42",
            EventName = "jobs.progressed",
            LegacyTypeIdentifier = typeof(DerivedProgressEvent).AssemblyQualifiedName,
            EntityPath = "progress-events",
            SubscriptionName = "mendmd-web",
            DeliveryCount = 3,
            CorrelationId = "correlation-42",
            DispatchMode = DistributedEventDispatchMode.Direct,
            CreatedAtUtc = DateTime.UtcNow
        };
        var eventData = new DerivedProgressEvent { JobId = "job-42", Percent = 75 };
        var incoming = new IncomingEventInfo(Guid.NewGuid(), messageContext.MessageId, messageContext.EventName,
                serializer.Serialize(eventData, typeof(DerivedProgressEvent)), messageContext.CreatedAtUtc)
            .SetCorrelationId(messageContext.CorrelationId!)
            .SetMessageContext(messageContext);

        await ((ISupportsEventBoxes)bus).ProcessFromInboxAsync(incoming, new InboxConfig());

        Assert.Equal(1, handler.Count);
        Assert.Equal(messageContext.MessageId, handler.Context?.MessageId);
        Assert.Equal(messageContext.EntityPath, handler.Context?.EntityPath);
        Assert.Equal(messageContext.SubscriptionName, handler.Context?.SubscriptionName);
        Assert.Equal(messageContext.DeliveryCount, handler.Context?.DeliveryCount);
        Assert.Equal(messageContext.CorrelationId, handler.Context?.CorrelationId);
    }

    [Fact]
    public void AzureOptions_RejectTopicWithoutSubscription_AndEntityPathMismatch()
    {
        var serializer = Resolve<IEventSerializer>();
        var ioc = new Moq.Mock<IIocManager>();
        var topicWithoutSubscription = new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test/;SharedAccessKeyName=Root;SharedAccessKey=key",
            EntityPath = "events",
            EntityKind = AzureServiceBusEntityKind.Topic
        };
        Assert.Throws<AbpException>(() => new AzureServiceBusDistributedEventBus(new(), topicWithoutSubscription, ioc.Object, serializer));

        var mismatchedPath = new AzureServiceBusOptions
        {
            ConnectionString = "Endpoint=sb://test/;EntityPath=other;SharedAccessKeyName=Root;SharedAccessKey=key",
            EntityPath = "events",
            EntityKind = AzureServiceBusEntityKind.Queue
        };
        Assert.Throws<AbpException>(() => new AzureServiceBusDistributedEventBus(new(), mismatchedPath, ioc.Object, serializer));
    }

    [EventName("jobs.progressed")]
    private class DerivedProgressEvent : BackgroundJobEventBase
    {
        public int Percent { get; set; }
    }

    private class BackgroundJobEventBase : EventData
    {
        public string? JobId { get; set; }
    }

    private sealed class BaseEventHandler(IDistributedEventContextAccessor accessor) : IDistributedEventHandler<BackgroundJobEventBase>
    {
        public int Count { get; private set; }
        public BackgroundJobEventBase? LastEvent { get; private set; }
        public DistributedEventMessageContext? Context { get; private set; }
        public Task HandleEventAsync(BackgroundJobEventBase eventData)
        {
            Count++;
            LastEvent = eventData;
            Context = accessor.Current;
            return Task.CompletedTask;
        }
    }
}
