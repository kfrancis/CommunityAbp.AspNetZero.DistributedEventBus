using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class OutboxInboxTests : DistributedEventBusTests
{
    [Fact]
    public async Task Publish_WithOutbox_ShouldPersistMessage()
    {
        var bus = Resolve<IDistributedEventBus>();
        await bus.PublishAsync(new TestEvent(), useOutbox: true);

        UsingDbContext(ctx =>
        {
            Assert.True(ctx.OutboxMessages.Any());
        });
    }

    [Fact]
    public async Task Publish_WithOutbox_ShouldNotDeliverImmediately()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        bus.Subscribe<TestEvent>(new TestEventHandler(() => handled = true));

        await bus.PublishAsync(new TestEvent(), useOutbox: true);
        // Allow any immediate synchronous handlers to run
        await Task.Yield();
        UsingDbContext(ctx => Assert.True(ctx.OutboxMessages.Any()));
        Assert.False(handled); // not delivered yet
    }

    [Fact]
    public async Task PollingOutboxSender_ShouldDeliverPersistedEvents()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        bus.Subscribe<TestEvent>(new TestEventHandler(() => handled = true));

        await bus.PublishAsync(new TestEvent(), useOutbox: true);

        // Speed up polling for the test
        var boxOptions = Resolve<AspNetZeroEventBusBoxesOptions>();
        boxOptions.OutboxPollingInterval = TimeSpan.FromMilliseconds(150);

        var options = Resolve<DistributedEventBusOptions>();
        var sender = Resolve<IOutboxSender>();

        await sender.StartAsync(options.Outboxes["Default"]);

        // Actively wait until handled or timeout
        var timeout = TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while (!handled && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(100);
        }

        Assert.True(handled, "Event was not dispatched from outbox within timeout.");
        await sender.StopAsync();
    }

    [EventName(nameof(TestEvent))]
    private class TestEvent : EventData { }

    private class TestEventHandler : IDistributedEventHandler<TestEvent>
    {
        private readonly System.Action _onHandle;
        public TestEventHandler(System.Action onHandle) => _onHandle = onHandle;
        public Task HandleEventAsync(TestEvent eventData)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }
}
