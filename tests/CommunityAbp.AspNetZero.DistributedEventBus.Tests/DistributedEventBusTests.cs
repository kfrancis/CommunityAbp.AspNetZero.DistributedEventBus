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
    public async Task PublishAsync_ShouldPublishDirectly_WhenNotUsingOutbox()
    {
        // Arrange
        var mockOutboxManager = Substitute.For<ISupportsEventBoxes>();
        var localBus = Resolve<IDistributedEventBus>();
        ReplaceService<ISupportsEventBoxes>(mockOutboxManager);

        var testEvent = new TestEvent();

        // Act
        await localBus.PublishAsync(testEvent, useOutbox: false);

        // Assert
        await mockOutboxManager.DidNotReceive().PublishFromOutboxAsync(
            Arg.Any<OutgoingEventInfo>(), Arg.Any<OutboxConfig>());
        // Optionally, assert direct publish logic if accessible
    }

    //[Fact]
    //public async Task Subscribe_ShouldInvokeHandler_WhenEventIsPublished()
    //{
    //    // Arrange
    //    var localBus = Resolve<IDistributedEventBus>();
    //    var wasHandled = false;

    //    localBus.Subscribe<TestEvent>(e => { wasHandled = true; return Task.CompletedTask; });

    //    // Act
    //    await localBus.PublishAsync(new TestEvent(), useOutbox: false);

    //    // Assert
    //    Assert.True(wasHandled);
    //}

    //[Fact]
    //public async Task Unsubscribe_ShouldNotInvokeHandler_WhenEventIsPublished()
    //{
    //    // Arrange
    //    var localBus = Resolve<IDistributedEventBus>();
    //    var wasHandled = false;
    //    Func<TestEvent, Task> handler = e => { wasHandled = true; return Task.CompletedTask; };

    //    localBus.Subscribe(handler);
    //    localBus.Unsubscribe(handler);

    //    // Act
    //    await localBus.PublishAsync(new TestEvent(), useOutbox: false);

    //    // Assert
    //    Assert.False(wasHandled);
    //}

    [Fact]
    public async Task PublishAsync_ShouldThrowArgumentNullException_WhenEventIsNull()
    {
        // Arrange
        var localBus = Resolve<IDistributedEventBus>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => localBus.PublishAsync<TestEvent>(null, useOutbox: false));
    }

    [Fact]
    public void TestEnvironment_ShouldResolveDistributedEventBus()
    {
        var localBus = Resolve<IDistributedEventBus>();
        Assert.NotNull(localBus);
    }

    [EventName(nameof(TestEvent))]
    private class TestEvent : EventData;
}
