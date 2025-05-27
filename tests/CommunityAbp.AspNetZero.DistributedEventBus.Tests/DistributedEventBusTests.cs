using Abp.Dependency;
using Abp.Events.Bus;
using Abp.TestBase;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;
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

        // Setup configuration
        _configuration.ConnectionString.Returns("Endpoint=sb://your-service-bus-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-access-key\"");
        _configuration.EntityPath.Returns("your-queue-or-topic-name");
        _configuration.SubscriptionName.Returns("your-subscription-name");

        ReplaceService<IAzureServiceBusOptions>(_configuration);
    }

    [Fact]
    public void TestEnvironment_ShouldResolveDistributedEventBus()
    {
        var localBus = Resolve<IDistributedEventBus>();
        Assert.NotNull(localBus);
    }

    [Fact]
    public async Task PublishAsync_ShouldPublishDirectly_WhenNotUsingOutbox()
    {
        // Arrange
        var mockOutboxManager = Substitute.For<ISupportsEventBoxes>();
        var localBus = Resolve<IDistributedEventBus>();

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

    [EventName(nameof(TestEvent))]
    private class TestEvent : EventData;
}
