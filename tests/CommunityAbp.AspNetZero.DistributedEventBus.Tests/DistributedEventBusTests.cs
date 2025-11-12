using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using NSubstitute;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

/// <summary>
///     Contains unit tests for verifying the behavior of the distributed event bus implementation using an in-memory test
///     environment.
/// </summary>
/// <remarks>
///     This test class ensures that the distributed event bus resolves correctly, publishes events as
///     expected, and handles subscription and unsubscription scenarios. It uses a stubbed Azure Service Bus configuration,
///     but the actual event bus implementation under test is replaced with an in-memory version for isolation and
///     repeatability. The tests cover direct publishing, handler invocation, and argument validation.
/// </remarks>
public class DistributedEventBusTests : AppTestBase<DistributedEventBusTestModule>
{
    public DistributedEventBusTests()
    {
        var configuration = Substitute.For<IAzureServiceBusOptions>();

        // Stub configuration (not actually used by in-memory bus replacement in tests)
        configuration.ConnectionString.Returns(
            "Endpoint=sb://your-service-bus-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-access-key");
        configuration.EntityPath.Returns("your-queue-or-topic-name");
        configuration.SubscriptionName.Returns("your-subscription-name");

        ReplaceService(configuration);
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
        bus.Subscribe(new TestEventHandler(() => handled = true));
        await bus.PublishAsync(new TestEvent(), useOutbox: false);
        Assert.True(handled);
    }

    [Fact]
    public async Task Unsubscribe_ShouldNotInvokeHandler_WhenEventIsPublished()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        var handler = new TestEventHandler(() => handled = true);
        var subscription = bus.Subscribe(handler);
        subscription.Dispose();
        await bus.PublishAsync(new TestEvent(), useOutbox: false);
        Assert.False(handled);
    }

    [Fact]
    public async Task PublishAsync_ShouldThrowArgumentNullException_WhenEventIsNull()
    {
        var bus = Resolve<IDistributedEventBus>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => bus.PublishAsync<TestEvent>(null!, useOutbox: false));
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
