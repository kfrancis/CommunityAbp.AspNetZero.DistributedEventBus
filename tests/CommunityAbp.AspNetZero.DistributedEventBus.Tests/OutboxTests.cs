using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests;

public class OutboxTests : AppTestBase<DistributedEventBusTestModule>
{
    [Fact]
    public async Task Outbox_Publish_StoresEvent_Then_PublishFromOutbox_Dispatches()
    {
        var bus = Resolve<IDistributedEventBus>();
        var options = Resolve<DistributedEventBusOptions>();

        // Register in-memory outbox
        options.Outboxes.Configure("test", cfg =>
        {
            cfg.ImplementationType = typeof(InMemoryOutbox);
            cfg.Selector = t => t == typeof(OutboxTestEvent);
            cfg.IsSendingEnabled = false; // manual publish
        });

        // Ensure IoC registration for outbox
        LocalIocManager.IocContainer.Register(
            Castle.MicroKernel.Registration.Component
                .For<InMemoryOutbox, IEventOutbox>()
                .LifestyleSingleton()
        );

        var handled = 0;
        bus.Subscribe(new OutboxTestHandler(() => handled++));

        await bus.PublishAsync(new OutboxTestEvent { Value = "X" }, useOutbox: true);

        var outbox = Resolve<InMemoryOutbox>();
        Assert.Single(outbox.Events);
        Assert.Equal(0, handled);

        var cfg = options.Outboxes["test"];
        await ((ISupportsEventBoxes)bus).PublishFromOutboxAsync(outbox.Events.First(), cfg);

        Assert.Equal(1, handled);
    }

    private class OutboxTestEvent
    {
        public string Value { get; set; }
    }

    private class OutboxTestHandler : IDistributedEventHandler<OutboxTestEvent>
    {
        private readonly Action _onHandle;
        public OutboxTestHandler(Action onHandle) => _onHandle = onHandle;
        public Task HandleEventAsync(OutboxTestEvent eventData)
        {
            _onHandle();
            return Task.CompletedTask;
        }
    }

    // Simple in-memory outbox
    private class InMemoryOutbox : IEventOutbox
    {
        private class State
        {
            public OutgoingEventInfo Event { get; }
            public string Status { get; set; } = "Pending";
            public int RetryCount { get; set; }
            public string? Error { get; set; }
            public DateTime? SentAt { get; set; }

            public State(OutgoingEventInfo evt) => Event = evt;
        }

        private readonly List<State> _states = new();
        private readonly object _lock = new();

        // For the test we expose the raw events
        public IReadOnlyList<OutgoingEventInfo> Events
        {
            get
            {
                lock (_lock)
                {
                    return _states.Select(s => s.Event).ToList();
                }
            }
        }

        public Task AddAsync(OutgoingEventInfo outgoingEvent, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _states.Add(new State(outgoingEvent));
            }
            return Task.CompletedTask;
        }

        public IReadOnlyList<OutgoingEventInfo> GetEvents()
        {
            lock (_lock)
            {
                return _states.Select(s => s.Event).ToList();
            }
        }

        public Task<IEnumerable<OutgoingEventInfo>> GetPendingAsync(int outboxBatchSize, CancellationToken ct)
        {
            lock (_lock)
            {
                var pending = _states
                    .Where(s => s.Status == "Pending")
                    .OrderBy(s => s.Event.CreationTime)
                    .Take(outboxBatchSize)
                    .Select(s => s.Event)
                    .ToList()
                    .AsEnumerable();
                return Task.FromResult<IEnumerable<OutgoingEventInfo>>(pending);
            }
        }

        public Task MarkFailedAsync(object id, string v, CancellationToken ct)
        {
            if (id is Guid guid)
            {
                lock (_lock)
                {
                    var state = _states.FirstOrDefault(s => s.Event.Id == guid);
                    if (state != null)
                    {
                        state.Status = "Failed";
                        state.RetryCount += 1;
                        state.Error = v;
                    }
                }
                return Task.CompletedTask;
            }
            throw new ArgumentException("id must be Guid", nameof(id));
        }

        public Task MarkSentAsync(object id, CancellationToken ct)
        {
            if (id is Guid guid)
            {
                lock (_lock)
                {
                    var state = _states.FirstOrDefault(s => s.Event.Id == guid);
                    if (state != null)
                    {
                        state.Status = "Sent";
                        state.SentAt = DateTime.UtcNow;
                        state.Error = null;
                    }
                }
                return Task.CompletedTask;
            }
            throw new ArgumentException("id must be Guid", nameof(id));
        }
    }
}
