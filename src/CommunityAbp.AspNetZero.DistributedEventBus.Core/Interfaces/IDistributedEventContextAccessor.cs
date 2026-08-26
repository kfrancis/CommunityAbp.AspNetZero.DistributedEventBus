using System;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>Provides message metadata to code executing inside an event handler.</summary>
public interface IDistributedEventContextAccessor
{
    DistributedEventMessageContext? Current { get; }
    IDisposable Push(DistributedEventMessageContext context);
}
