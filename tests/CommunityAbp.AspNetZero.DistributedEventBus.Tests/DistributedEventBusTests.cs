using Abp.Events.Bus; // Needed for EventData base class
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using NSubstitute;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class DistributedEventBusTests : AppTestBase<DistributedEventBusTestModule>
{
    private readonly IAzureServiceBusOptions _configuration;

    public DistributedEventBusTests()
    {
        _configuration = Substitute.For<IAzureServiceBusOptions>();

        // Stub configuration (not actually used by in-memory bus replacement in tests)
        _configuration.ConnectionString.Returns("Endpoint=sb://your-service-bus-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-access-key");
        _configuration.EntityPath.Returns("your-queue-or-topic-name");
        _configuration.SubscriptionName.Returns("your-subscription-name");

        ReplaceService(_configuration);
    }

    [Fact]
    public void TestEnvironment_ShouldResolveDistributedEventBus()
    {
        var bus = Resolve<IDistributedEventBus>();
        Assert.NotNull(bus);
    }

    [Fact]
    public async Task PublishAsync_ShouldPublishDirectly_WhenNotUsingOutbox()
    {
        var bus = Resolve<IDistributedEventBus>();
        bus.Subscribe(new TestEventHandler(() => { }));
        await bus.PublishAsync(new TestEvent(), useOutbox: false);
    }

    [Fact]
    public async Task ManualSubscribe_ShouldInvokeHandler_WhenEventIsPublished()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        bus.Subscribe<TestEvent>(new TestEventHandler(() => handled = true));
        await bus.PublishAsync(new TestEvent(), useOutbox: false);
        Assert.True(handled);
    }

    [Fact]
    public async Task Unsubscribe_ShouldNotInvokeHandler_WhenEventIsPublished()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        var handler = new TestEventHandler(() => handled = true);
        var subscription = bus.Subscribe<TestEvent>(handler);
        subscription.Dispose();
        await bus.PublishAsync(new TestEvent(), useOutbox: false);
        Assert.False(handled);
    }

    [Fact]
    public async Task PublishAsync_ShouldThrowArgumentNullException_WhenEventIsNull()
    {
        var bus = Resolve<IDistributedEventBus>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync<TestEvent>(null, useOutbox: false));
    }

    [EventName(nameof(TestEvent))]
    private class TestEvent : EventData;

    private class TestEventHandler : IDistributedEventHandler<TestEvent>
    {
        private readonly Action _onHandle;
        public TestEventHandler(Action onHandle) => _onHandle = onHandle;
        public Task HandleEventAsync(TestEvent eventData)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }
}
