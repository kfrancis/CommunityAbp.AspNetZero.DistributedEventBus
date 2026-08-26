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

    private sealed class SqlServerEvent;
}
