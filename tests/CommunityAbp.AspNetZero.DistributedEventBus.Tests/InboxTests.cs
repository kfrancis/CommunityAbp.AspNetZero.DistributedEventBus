using Abp.Events.Bus;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

// for IInboxProcessor

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class InboxTests : AppTestBase // use non-generic wrapper defined in project
{
    [Fact]
    public async Task Outbox_Event_Can_Be_Received_By_Inbox_And_Dispatched()
    {
        var bus = Resolve<IDistributedEventBus>();
        var options = Resolve<DistributedEventBusOptions>();
        var boxOpts = Resolve<AspNetZeroEventBusBoxesOptions>();

        // Configure inbox (none by default in test base)
        options.Inboxes.Configure("DefaultInbox", cfg =>
        {
            cfg.ImplementationType = typeof(EfCoreEventInbox);
            cfg.EventSelector = _ => true; // accept all
            cfg.IsProcessingEnabled = true;
        });

        // Register EF Core inbox implementation if not already registered
        if (!LocalIocManager.IsRegistered<IEventInbox>())
        {
            LocalIocManager.IocContainer.Register(
                Component
                    .For<IEventInbox, EfCoreEventInbox>()
                    .LifestyleTransient());
        }

        // Speed up polling
        boxOpts.InboxPollingInterval = TimeSpan.FromMilliseconds(100);

        var handled = 0;
        bus.Subscribe(new TestInboxEventHandler(() => handled++));

        // Publish using outbox so it is persisted but NOT dispatched yet
        await bus.PublishAsync(new TestInboxEvent { Value = "Ping" }, useOutbox: true);
        Assert.Equal(0, handled);

        // Read the outbox row
        var outboxRow = UsingDbContext(ctx => ctx.OutboxMessages.FirstOrDefault());
        Assert.NotNull(outboxRow);

        // Simulate broker delivery by persisting to inbox
        var incoming = new IncomingEventInfo(
            Guid.NewGuid(), // new inbox event id
            Guid.NewGuid().ToString(), // synthetic MessageId from broker
            outboxRow.EventName, // use stored type identifier
            outboxRow.EventData,
            DateTime.UtcNow);
        var inbox = Resolve<IEventInbox>();
        await inbox.AddAsync(incoming);

        // Verify inbox has pending event
        UsingDbContext(ctx => Assert.True(ctx.InboxMessages.Any(m => m.Status == "Pending")));

        // Start polling processor to consume inbox events
        var processor = Resolve<IInboxProcessor>();
        await processor.StartAsync(options.Inboxes["DefaultInbox"]);

        // Wait until handler called or timeout
        var timeoutAt = DateTime.UtcNow.AddSeconds(5);
        while (handled == 0 && DateTime.UtcNow < timeoutAt)
        {
            await Task.Delay(100);
        }

        Assert.Equal(1, handled);

        // Ensure inbox message marked processed
        UsingDbContext(ctx => Assert.True(ctx.InboxMessages.Any(m => m.Status == "Processed")));

        await processor.StopAsync();
    }

    private class TestInboxEvent : EventData
    {
        public string Value { get; set; } = string.Empty;
    }

    private class TestInboxEventHandler : IDistributedEventHandler<TestInboxEvent>
    {
        private readonly Action _onHandle;
        public TestInboxEventHandler(Action onHandle) => _onHandle = onHandle;

        public Task HandleEventAsync(TestInboxEvent eventData)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }
}
