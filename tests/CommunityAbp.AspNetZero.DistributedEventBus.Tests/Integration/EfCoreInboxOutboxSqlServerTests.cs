using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public sealed class EfCoreInboxOutboxSqlServerTests : IAsyncLifetime
{
    private readonly SqlServerFixture _fixture;

    public EfCoreInboxOutboxSqlServerTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        return _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Outbox_Claim_And_Completion_Use_Server_Side_Updates()
    {
        var eventId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateDbContext())
        {
            var outbox = new EfCoreEventOutbox(writeContext);
            await outbox.AddAsync(new OutgoingEventInfo(
                eventId,
                typeof(SqlServerEvent).AssemblyQualifiedName!,
                [1, 2, 3],
                DateTime.UtcNow));
        }

        await using (var claimContext = _fixture.CreateDbContext())
        {
            var outbox = new EfCoreEventOutbox(claimContext);
            var pending = await outbox.GetPendingAsync(10, CancellationToken.None);

            Assert.Single(pending);
            Assert.True(await outbox.TryClaimAsync(eventId, CancellationToken.None));
            Assert.False(await outbox.TryClaimAsync(eventId, CancellationToken.None));
            await outbox.MarkSentAsync(eventId, CancellationToken.None);
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.OutboxMessages.SingleAsync(x => x.Id == eventId);
        Assert.Equal("Sent", saved.Status);
        Assert.NotNull(saved.SentAt);
        Assert.Null(saved.ProcessingStartedAt);
    }

    [Fact]
    public async Task Inbox_Expired_Claim_Is_Requeued_And_Can_Be_Processed()
    {
        var eventId = Guid.NewGuid();

        await using (var writeContext = _fixture.CreateDbContext())
        {
            var inbox = new EfCoreEventInbox(writeContext);
            await inbox.AddAsync(new IncomingEventInfo(
                eventId,
                Guid.NewGuid().ToString("N"),
                typeof(SqlServerEvent).AssemblyQualifiedName!,
                [4, 5, 6],
                DateTime.UtcNow));
            Assert.True(await inbox.TryClaimAsync(eventId, CancellationToken.None));

            await writeContext.InboxMessages
                .Where(x => x.Id == eventId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    x => x.ProcessingStartedAt,
                    DateTime.UtcNow - TimeSpan.FromMinutes(10)));
        }

        await using (var claimContext = _fixture.CreateDbContext())
        {
            var inbox = new EfCoreEventInbox(claimContext);
            Assert.Equal(1, await inbox.RequeueExpiredClaimsAsync(TimeSpan.FromMinutes(5), CancellationToken.None));
            Assert.True(await inbox.TryClaimAsync(eventId, CancellationToken.None));
            await inbox.MarkProcessedAsync(eventId, CancellationToken.None);
        }

        await using var verifyContext = _fixture.CreateDbContext();
        var saved = await verifyContext.InboxMessages.SingleAsync(x => x.Id == eventId);
        Assert.Equal("Processed", saved.Status);
        Assert.NotNull(saved.ProcessedAt);
        Assert.Null(saved.ProcessingStartedAt);
    }

    [Fact]
    public async Task Inbox_Persists_Broker_Message_Context()
    {
        var eventId = Guid.NewGuid();
        var receivedAt = DateTime.UtcNow;
        var messageContext = new DistributedEventMessageContext
        {
            MessageId = "broker-message-42",
            EventName = "jobs.progressed",
            LegacyTypeIdentifier = typeof(SqlServerEvent).AssemblyQualifiedName,
            EntityPath = "progress-events",
            SubscriptionName = "mendmd-web",
            DeliveryCount = 2,
            CorrelationId = "correlation-42",
            DispatchMode = DistributedEventDispatchMode.Direct,
            CreatedAtUtc = receivedAt
        };

        await using (var writeContext = _fixture.CreateDbContext())
        {
            var inbox = new EfCoreEventInbox(writeContext);
            await inbox.AddAsync(new IncomingEventInfo(eventId, messageContext.MessageId, messageContext.EventName,
                    [4, 5, 6], receivedAt)
                .SetCorrelationId(messageContext.CorrelationId!)
                .SetMessageContext(messageContext));
        }

        await using var readContext = _fixture.CreateDbContext();
        var pending = await new EfCoreEventInbox(readContext).GetPendingAsync(10);
        var restored = Assert.Single(pending);

        Assert.Equal(messageContext.MessageId, restored.MessageContext?.MessageId);
        Assert.Equal(messageContext.LegacyTypeIdentifier, restored.MessageContext?.LegacyTypeIdentifier);
        Assert.Equal(messageContext.EntityPath, restored.MessageContext?.EntityPath);
        Assert.Equal(messageContext.SubscriptionName, restored.MessageContext?.SubscriptionName);
        Assert.Equal(messageContext.DeliveryCount, restored.MessageContext?.DeliveryCount);
        Assert.Equal(messageContext.CorrelationId, restored.MessageContext?.CorrelationId);
        Assert.Equal(messageContext.DispatchMode, restored.MessageContext?.DispatchMode);
    }

    private sealed class SqlServerEvent;
}
