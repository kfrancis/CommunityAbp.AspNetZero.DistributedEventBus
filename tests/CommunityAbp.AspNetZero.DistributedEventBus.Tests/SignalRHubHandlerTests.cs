using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models; // for EventNameAttribute
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using Microsoft.AspNetCore.SignalR;
using Castle.MicroKernel.Registration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class SignalRHubHandlerTests : AppTestBase<DistributedEventBusTestModule>
{
    [Fact]
    public async Task Hub_Implementing_IDistributedEventHandler_Should_Auto_Subscribe_And_Handle_Event()
    {
        // Register dummy hub context before bus resolution so auto-subscribe can construct hub
        LocalIocManager.IocContainer.Register(
            Component.For<IHubContext<BackgroundJobEventHub>>()
                     .ImplementedBy<DummyHubContext>()
                     .LifestyleSingleton()
        );

        var bus = Resolve<IDistributedEventBus>();
        var hub = Resolve<BackgroundJobEventHub>();
        Assert.NotNull(bus);
        Assert.NotNull(hub);
        Assert.Equal(0, hub.CallCount);
        Assert.Equal(0, NoOpClientProxy.SendCount);

        await bus.PublishAsync(new BackgroundJobEventBase(), useOutbox: false);

        Assert.Equal(1, hub.CallCount);
        Assert.Equal(1, NoOpClientProxy.SendCount); // broadcast attempted via hub context
    }
}

// Event base (can hold common props later)
[EventName("BackgroundJobEventBase")] // optional explicit name
public class BackgroundJobEventBase : EventData { }

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
        await _context.Clients.All.SendCoreAsync("BackgroundJobEvent", new object[] { eventData });
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
    private readonly IClientProxy _proxy = new NoOpClientProxy();
    public IClientProxy All => _proxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Client(string connectionId) => _proxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;
    public IClientProxy Group(string groupName) => _proxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;
    public IClientProxy User(string userId) => _proxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
}

public class NoOpGroupManager : IGroupManager
{
    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
