using System.Threading.Tasks;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using Microsoft.AspNetCore.SignalR;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models; // for EventNameAttribute

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class SignalRHubHandlerTests : AppTestBase<DistributedEventBusTestModule>
{
    [Fact]
    public async Task Hub_Implementing_IDistributedEventHandler_Should_Auto_Subscribe_And_Handle_Event()
    {
        var bus = Resolve<IDistributedEventBus>();
        var hub = Resolve<BackgroundJobEventHub>();
        Assert.NotNull(bus);
        Assert.NotNull(hub);
        Assert.Equal(0, hub.CallCount);

        await bus.PublishAsync(new BackgroundJobEventBase(), useOutbox: false);

        Assert.Equal(1, hub.CallCount);
    }
}

// Event base (can hold common props later)
[EventName("BackgroundJobEventBase")] // optional explicit name
public class BackgroundJobEventBase : EventData { }

// Hub that directly implements distributed handler interface
public class BackgroundJobEventHub : Hub, IDistributedEventHandler<BackgroundJobEventBase>
{
    public int CallCount { get; private set; }

    public Task HandleEventAsync(BackgroundJobEventBase eventData)
    {
        CallCount++;
        // In real usage you would broadcast to clients:
        // return Clients.All.SendAsync("BackgroundJobEvent", eventData);
        return Task.CompletedTask;
    }
}
