using Abp.Events.Bus;
using Castle.MicroKernel.Registration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using Microsoft.AspNetCore.SignalR;
// for EventNameAttribute

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class SignalRHubHandlerTests : AppTestBase<DistributedEventBusTestModule>
{
    [Fact]
    public async Task Hub_Implementing_IDistributedEventHandler_Should_Handle_Event_After_Manual_Subscribe()
    {
        LocalIocManager.IocContainer.Register(
            Component.For<IHubContext<BackgroundJobEventHub>>()
                .ImplementedBy<DummyHubContext>()
                .LifestyleSingleton()
        );
        var bus = Resolve<IDistributedEventBus>();
        var hub = Resolve<BackgroundJobEventHub>();
        bus.Subscribe(hub); // manual subscription
        Assert.NotNull(bus);
        Assert.NotNull(hub);
        Assert.Equal(0, hub.CallCount);
        Assert.Equal(0, NoOpClientProxy.SendCount);

        await bus.PublishAsync(new BackgroundJobEventBase(), useOutbox: false);

        Assert.Equal(1, hub.CallCount);
        Assert.Equal(1, NoOpClientProxy.SendCount);
    }
}

// Event base (can hold common props later)
[EventName("BackgroundJobEventBase")] // optional explicit name
public class BackgroundJobEventBase : EventData
{
}

// Hub that directly implements distributed handler interface
public class BackgroundJobEventHub : Hub, IDistributedEventHandler<BackgroundJobEventBase>
{
    private readonly IHubContext<BackgroundJobEventHub> _context; // needed for broadcasting

    public BackgroundJobEventHub(IHubContext<BackgroundJobEventHub> context)
    {
        _context = context;
    }

    public int CallCount { get; private set; }

    public async Task HandleEventAsync(BackgroundJobEventBase eventData)
    {
        CallCount++;
        // Broadcast to all clients (dummy context in tests counts calls)
        await _context.Clients.All.SendCoreAsync("BackgroundJobEvent", [eventData]);
    }
}

// Dummy implementations to satisfy SignalR abstractions for unit testing broadcasting behavior
public class DummyHubContext : IHubContext<BackgroundJobEventHub>
{
    public IHubClients Clients { get; } = new NoOpHubClients();
    public IGroupManager Groups { get; } = new NoOpGroupManager();
}

public class NoOpHubClients : IHubClients
{
    public IClientProxy All { get; } = new NoOpClientProxy();

    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => All;
    public IClientProxy Client(string connectionId) => All;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => All;
    public IClientProxy Group(string groupName) => All;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => All;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => All;
    public IClientProxy User(string userId) => All;
    public IClientProxy Users(IReadOnlyList<string> userIds) => All;
}

public class NoOpGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task RemoveFromGroupAsync(string connectionId, string groupName,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public class NoOpClientProxy : IClientProxy
{
    public static int SendCount { get; private set; }

    public Task SendCoreAsync(string method, object[] args, CancellationToken cancellationToken = default)
    {
        SendCount++;
        return Task.CompletedTask;
    }
}
