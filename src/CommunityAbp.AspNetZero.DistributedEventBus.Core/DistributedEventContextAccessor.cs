using System;
using System.Threading;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public sealed class DistributedEventContextAccessor : IDistributedEventContextAccessor
{
    private static readonly AsyncLocal<DistributedEventMessageContext?> CurrentContext = new();
    public DistributedEventMessageContext? Current => CurrentContext.Value;

    public IDisposable Push(DistributedEventMessageContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly DistributedEventMessageContext? _previous;
        private int _disposed;
        public Scope(DistributedEventMessageContext? previous) => _previous = previous;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) CurrentContext.Value = _previous;
        }
    }
}
