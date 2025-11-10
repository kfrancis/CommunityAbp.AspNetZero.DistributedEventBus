using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

/// <summary>
/// Verifies a full manual pipeline: event persisted to outbox, copied to inbox, then processed from inbox.
/// Uses ISupportsEventBoxes APIs directly (no polling workers) to exercise both sides in combination.
/// </summary>
public class CombinedOutboxInboxTests : AppTestBase
{
    [Fact]
    public async Task Outbox_To_Inbox_Flow_Manual_Should_Dispatch_Once()
    {
        var bus = Resolve<IDistributedEventBus>();
        var boxBus = (ISupportsEventBoxes)bus;
        var options = Resolve<DistributedEventBusOptions>();

        // Ensure inbox configured
        options.Inboxes.Configure("TestInbox", cfg =>
        {
            cfg.ImplementationType = typeof(EfCoreEventInbox);
            cfg.EventSelector = _ => true;
            cfg.IsProcessingEnabled = false; // manual in this test
        });

        // Ensure EF inbox registration
        if (!LocalIocManager.IsRegistered<IEventInbox>())
        {
            LocalIocManager.IocContainer.Register(
                Castle.MicroKernel.Registration.Component
                    .For<IEventInbox, EfCoreEventInbox>()
                    .LifestyleTransient());
        }

        var handled = 0;
        bus.Subscribe(new CombinedTestEventHandler(() => handled++));

        // Publish with outbox so only persisted
        await bus.PublishAsync(new CombinedTestEvent { Value = "Flow" }, useOutbox: true);
        Assert.Equal(0, handled);

        // Get EF outbox row
        var outboxRow = UsingDbContext(ctx => ctx.OutboxMessages.First());
        Assert.Equal("Pending", outboxRow.Status);

        // Simulate broker transfer: create inbox record from outbox row.
        var incoming = new IncomingEventInfo(
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            outboxRow.EventName,
            outboxRow.EventData,
            DateTime.UtcNow);
        var inbox = Resolve<IEventInbox>();
        await inbox.AddAsync(incoming);

        // Verify inbox pending
        UsingDbContext(ctx => Assert.True(ctx.InboxMessages.Any(m => m.Status == "Pending")));

        // Manually process via bus (no status change yet)
        await boxBus.ProcessFromInboxAsync(incoming, options.Inboxes["TestInbox"]);
        Assert.Equal(1, handled);

        // Mark processed to finish flow
        await inbox.MarkProcessedAsync(incoming.Id, CancellationToken.None);

        UsingDbContext(ctx => Assert.True(ctx.InboxMessages.Any(m => m.Id == incoming.Id && m.Status == "Processed")));
    }

    private class CombinedTestEvent : EventData { public string Value { get; set; } = string.Empty; }

    private class CombinedTestEventHandler : IDistributedEventHandler<CombinedTestEvent>
    {
        private readonly Action _onHandle;
        public CombinedTestEventHandler(Action onHandle) => _onHandle = onHandle;
        public Task HandleEventAsync(CombinedTestEvent eventData)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }
}
