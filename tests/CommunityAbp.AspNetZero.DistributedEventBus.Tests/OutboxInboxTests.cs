using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using Abp.Events.Bus;
// Inbox/Outbox persistence currently disabled in test module configuration.
namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class OutboxInboxTests : DistributedEventBusTests
{
    // Keep only default (no outbox) behavior test; others skipped until inbox/outbox completed.
    [Fact]
    public async Task Publish_Default_ShouldNotPersistMessage()
    {
        var bus = Resolve<IDistributedEventBus>();
        await bus.PublishAsync(new TestEvent());
        UsingDbContext(ctx => Assert.False(ctx.OutboxMessages.Any(), "Outbox should be empty when not requesting useOutbox."));
    }

    [Fact(Skip = "Outbox feature disabled in test module (EF outbox registration commented out)")]
    public async Task Publish_WithOutbox_ShouldPersistMessage()
    {
        var bus = Resolve<IDistributedEventBus>();
        await bus.PublishAsync(new TestEvent(), useOutbox: true);
        UsingDbContext(ctx => { Assert.True(ctx.OutboxMessages.Any()); });
    }

    [Fact(Skip = "Outbox feature disabled in test module (EF outbox registration commented out)")]
    public async Task Publish_WithOutbox_ShouldNotDeliverImmediately()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        bus.Subscribe<TestEvent>(new TestEventHandler(() => handled = true));
        await bus.PublishAsync(new TestEvent(), useOutbox: true);
        await Task.Yield();
        UsingDbContext(ctx => Assert.True(ctx.OutboxMessages.Any()));
        Assert.False(handled);
    }

    [Fact(Skip = "Outbox feature disabled in test module (EF outbox registration commented out)")]
    public async Task PollingOutboxSender_ShouldDeliverPersistedEvents()
    {
        var bus = Resolve<IDistributedEventBus>();
        var handled = false;
        bus.Subscribe<TestEvent>(new TestEventHandler(() => handled = true));
        await bus.PublishAsync(new TestEvent(), useOutbox: true);
        var timeout = TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while (!handled && DateTime.UtcNow - start < timeout)
        {
            await Task.Delay(100);
        }
        Assert.True(handled);
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
