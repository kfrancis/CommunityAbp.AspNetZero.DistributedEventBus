using Abp.Events.Bus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

// added for EventNameAttribute

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

[EventName("AutoTestEvent")] // optional explicit name
public class AutoTestEvent : EventData
{
}

public class AutoTestEventHandler : IDistributedEventHandler<AutoTestEvent>
{
    public int CallCount { get; private set; }

    public Task HandleEventAsync(AutoTestEvent eventData)
    {
        CallCount++;
        return Task.CompletedTask;
    }

    public void Reset() => CallCount = 0;
}
