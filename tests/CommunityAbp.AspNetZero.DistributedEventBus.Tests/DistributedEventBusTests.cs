using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using NSubstitute;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class DistributedEventBusTests : AppTestBase
{
    [Fact]
    public async Task PublishAsync_ShouldAddToOutbox_WhenUsingOutbox()
    {
        // Arrange
        var mockOutboxManager = Substitute.For<ISupportsEventBoxes>();
        var localBus = Resolve<IDistributedEventBus>();

        // Replace the outbox manager with our mock
        //ReplaceService(mockOutboxManager);

        var testEvent = new TestEvent();

        // Act
        await localBus.PublishAsync(testEvent, useOutbox: true);

        // Assert
        await mockOutboxManager.Received(1).PublishFromOutboxAsync(
            Arg.Is<OutgoingEventInfo>(e => e.EventName == nameof(TestEvent)),
            Arg.Any<OutboxConfig>());
    }


    [EventName(nameof(TestEvent))]
    private class TestEvent : EventData;
}
