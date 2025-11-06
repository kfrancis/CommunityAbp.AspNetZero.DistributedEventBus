using Abp.Events.Bus.Handlers;
using Abp.Threading;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Adapters;

// Adapter to bridge async distributed handler into ABP's sync event pipeline.
public sealed class AbpDistributedEventHandlerAdapter<TEvent> : IEventHandler<TEvent>
{
    private readonly IDistributedEventHandler<TEvent> _inner;

    public AbpDistributedEventHandlerAdapter(IDistributedEventHandler<TEvent> inner)
    {
        _inner = inner;
    }

    public void HandleEvent(TEvent eventData)
    {
        AsyncHelper.RunSync(() => _inner.HandleEventAsync(eventData));
    }
}

