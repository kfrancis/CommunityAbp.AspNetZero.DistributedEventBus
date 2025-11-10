# CommunityAbp.DistributedEventBus

Reliable distributed domain events for ABP / AspNet Zero using the Inbox / Outbox pattern and (optionally) Azure Service Bus.

Targets:
- .NET Standard 2.0 (broad compatibility)
- .NET 8 (modern runtime)

---
## Key Features
- Distributed event bus abstraction (`IDistributedEventBus`)
- Optional Azure Service Bus transport (`AzureServiceBusDistributedEventBus`)
- Outbox & Inbox persistence abstraction (plug in any storage)
- Configurable multiple Outboxes / Inboxes with filtering predicates
- Polling sender / processor manager abstractions (`IOutboxSender`, `IInboxProcessor`)
- Event boxing interfaces (`ISupportsEventBoxes`) for replay / reliability
- Strongly typed async handlers (`IDistributedEventHandler<TEvent>`) + adapter for ABP pipeline
- Optional stable logical names via `[EventName]` attribute
- CancellationToken‑ready method overloads (tokens currently reserved for future async orchestration)
- Test friendly (replace the bus with an in‑memory implementation)

> NOTE: Current Azure implementation publishes directly to Service Bus even when `useOutbox == true` (it first calls base which may persist to an outbox, then always sends). If you intend a strict store‑and‑forward flow, adapt that behavior.

---
## Architecture Overview
```
Domain Code  -->  IDistributedEventBus.PublishAsync
   |                    |
   | (useOutbox=true)   | (useOutbox=false)
   v                    v
 Outbox (DB)        Immediate Dispatch ---------------> Local in‑memory handlers
   |                             \
   | (IOutboxSender)              \
   v                               v
Azure Service Bus  <---------  AzureServiceBusDistributedEventBus
   |
   |  (subscription / queue)
   v
Inbox (DB)  <-- (IInboxProcessor pulls) --> Local Dispatch -> Handlers
```

### Core Concepts
| Concept | Implementation / Type | Notes |
|--------|------------------------|-------|
| Distributed Bus | `DistributedEventBusBase` | Maintains in‑memory handler map; manages outbox/inbox dispatch.
| Azure Transport | `AzureServiceBusDistributedEventBus` | Wraps Service Bus client & subscription processor.
| Outbox Config | `OutboxConfig` (dictionary on `DistributedEventBusOptions.Outboxes`) | `Selector` decides which event types are captured.
| Inbox Config | `InboxConfig` (dictionary on `DistributedEventBusOptions.Inboxes`) | `EventSelector` decides which event types are stored.
| Outgoing Event | `OutgoingEventInfo` | Serialized as UTF‑8 JSON bytes.
| Incoming Event | `IncomingEventInfo` | Stored prior to handler dispatch (if inbox configured).
| Event Naming | `[EventName("Logical.Name")]` | Falls back to `FullName` when attribute absent.
| Handlers | `IDistributedEventHandler<TEvent>` | Always async; sync bridging via adapter.

---
## Packages (Current Layout)
- `CommunityAbp.AspNetZero.DistributedEventBus.Core`
- `CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus`
- `CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore` (DB context + tables)
- Test infrastructure in `tests/*`

---
## Installation (Example)
Add packages:
```
dotnet add package CommunityAbp.AspNetZero.DistributedEventBus.Core
# Optional
dotnet add package CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
# Persistence (if using provided EF implementation)
dotnet add package CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore
```

Register ABP modules (simplified):
```
[DependsOn(
    typeof(AspNetZeroDistributedEventBusModule),
    typeof(AzureDistributedEventServiceBusModule), // if Azure transport
    typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule) // if EF persistence
)]
public class MyAppModule : AbpModule
{
    public override void PreInitialize()
    {
        var options = IocManager.Resolve<DistributedEventBusOptions>();

        options.Outboxes.Configure("Default", o =>
        {
            o.DatabaseName = "Default"; // logical DB tag
            o.ImplementationType = typeof(EfCoreEventOutbox); // implements IEventOutbox
            o.Selector = eventType => true;
            o.IsSendingEnabled = true;
        });

        options.Inboxes.Configure("Default", i =>
        {
            i.DatabaseName = "Default";
            i.ImplementationType = typeof(EfCoreEventInbox); // implements IEventInbox
            i.EventSelector = eventType => true;
            i.IsProcessingEnabled = true;
        });
    }
}
```

`appsettings.json` snippet for Azure Service Bus:
```
"AzureServiceBus": {
  "ConnectionString": "Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<KeyName>;SharedAccessKey=<Key>",
  "EntityPath": "app-events",
  "SubscriptionName": "default"
}
```

---
## Production Setup (EF Core, Migrations & Background Processing)
1. Connection string: Add a named connection string (e.g. `DistributedEventBus`) for SQL Server.
2. Ensure module registration (`AspNetZeroDistributedEventEntityFrameworkCoreModule`) so `DistributedEventBusDbContext` is added.
3. Migrations: This repo includes an initial migration (`InitialInboxOutbox`). If consuming as source, run:
   - `dotnet ef database update -p src/CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore -s <YourHostProject>`
   If consuming via NuGet and you want schema inside your app DB, either:
   - Reference the EF project directly and run update, or
   - Copy entities + create migrations in your own DbContext.
4. Startup initialization: Optionally call `context.Database.Migrate()` early (e.g., in a hosted service) to auto create tables.
5. Configure outbox & inbox (see Installation) and register implementations (`EfCoreEventOutbox`, `EfCoreEventInbox`).
6. Start background loops:
```
public class EventBoxesHostedService : IHostedService
{
    private readonly IOutboxSender _outboxSender;
    private readonly IInboxProcessor _inboxProcessor;
    private readonly DistributedEventBusOptions _opts;

    public EventBoxesHostedService(IOutboxSender outboxSender, IInboxProcessor inboxProcessor, DistributedEventBusOptions opts)
    {
        _outboxSender = outboxSender;
        _inboxProcessor = inboxProcessor;
        _opts = opts;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        foreach (var outbox in _opts.Outboxes.Values.Where(o => o.IsSendingEnabled))
            await _outboxSender.StartAsync(outbox);
        foreach (var inbox in _opts.Inboxes.Values.Where(i => i.IsProcessingEnabled))
            await _inboxProcessor.StartAsync(inbox);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await _outboxSender.StopAsync();
        await _inboxProcessor.StopAsync();
    }
}
```
7. Service registration (example):
```
services.AddHostedService<EventBoxesHostedService>();
```
8. Idempotency: Inbox enforces unique `MessageId` index. Provide stable broker message IDs.
9. Correlation IDs: Set via `OutgoingEventInfo.SetCorrelationId()` before publishing; propagate to logs.
10. Health checks (optional): Add checks for pending failures in inbox/outbox tables.

---
## Defining & Handling Events
```
[EventName("Orders.Created")] // optional logical name
public class OrderCreatedEvent : EventData
{
    public Guid OrderId { get; set; }
    public decimal Total { get; set; }
}

public class OrderCreatedHandler : IDistributedEventHandler<OrderCreatedEvent>
{
    public Task HandleEventAsync(OrderCreatedEvent evt)
    {
        // Idempotent side effects here
        return Task.CompletedTask;
    }
}
```

### Publishing
```
await _distributedEventBus.PublishAsync(new OrderCreatedEvent
{
    OrderId = order.Id,
    Total = order.Total
}, onUnitOfWorkComplete: false, useOutbox: true);
```
Parameters:
- `onUnitOfWorkComplete` (currently not wired to a UoW callback; placeholder for future integration)
- `useOutbox` controls whether event is persisted to matching outbox(es) (`OutboxConfig.Selector`) or dispatched immediately.

### Subscribing
```
_distributedEventBus.Subscribe(new OrderCreatedHandler());
```
Returns `IDisposable` for unsubscription.

---
## Azure Service Bus Specifics
When using `AzureServiceBusDistributedEventBus`:
- Publish override sends to Service Bus each call (independent of outbox usage) after base logic.
- Handlers are also invoked by Azure subscription processor messages (in addition to direct dispatch if you also publish without outbox).
- If an inbox implementation is registered & passed into the bus, incoming messages are first stored as `IncomingEventInfo`.
- Subscription requires `SubscriptionName` when using topics. For queues omit or adapt.

### Duplicate / Idempotency
Use event IDs / correlation IDs (`SetCorrelationId`) + unique constraints in inbox storage to ensure single processing.

---
## Testing
For isolation, tests can replace the Azure bus with an in‑memory variant:
```
Configuration.ReplaceService<IDistributedEventBus, InMemoryDistributedEventBus>(DependencyLifeStyle.Transient);
```
Example test (see `DistributedEventBusTests.cs`):
```
var bus = Resolve<IDistributedEventBus>();
var handled = false;
bus.Subscribe(new TestEventHandler(() => handled = true));
await bus.PublishAsync(new TestEvent(), useOutbox: false);
Assert.True(handled);
```

---
## Extension Points
| Interface | Purpose |
|-----------|---------|
| `IEventOutbox` | Persist outgoing events (implement Add / Retrieve / MarkSent patterns). |
| `IEventInbox` | Persist incoming broker events for idempotent processing. |
| `IOutboxSender` | Strategy to read pending outbox rows and publish (polling etc.). |
| `IInboxProcessor` | Strategy that pulls from inbox storage and dispatches to handlers. |
| `IDistributedEventBus` | Replace with alternate transports (RabbitMQ, Kafka, etc.). |

Add implementation type to config (`OutboxConfig.ImplementationType`, `InboxConfig.ImplementationType`).

---
## Correlation & Tracing
Both `OutgoingEventInfo` and `IncomingEventInfo` expose correlation helpers. Attach before persisting.

---
## Roadmap / Ideas
- Respect `onUnitOfWorkComplete` via ABP unit of work callbacks
- Retry / exponential backoff policies
- Batch send & receive operations
- Native instrumentation (ActivitySource / metrics)
- Exactly‑once EF implementations
- Additional transports (RabbitMQ, Kafka, Event Hubs)

---
## Limitations (Current)
- Azure bus always publishes directly (even with outbox).
- No default hosted service wiring included besides sample above.

---
## Sample Minimal Flow (Pseudo EF Outbox Sender)
```
foreach (var pending in await _outboxStore.GetUnsentAsync(batchSize))
{
    await _azureBus.PublishFromOutboxAsync(pending, outboxConfig);
    await _outboxStore.MarkSentAsync(pending.Id);
}
```

---
## Event Naming
`EventNameAttribute.GetNameOrDefault(type)` uses attribute name or `FullName`. Use stable logical names to decouple assembly refactors from wire contracts.

---
## Contributing
1. Fork & clone
2. Create feature branch
3. Add tests
4. Submit PR

---
## License
MIT © 2025 Kori Francis

---
## References
- Azure Service Bus: https://learn.microsoft.com/azure/service-bus-messaging/
- Transactional Outbox Pattern: https://microservices.io/patterns/data/transactional-outbox.html
- ABP Framework: https://abp.io/
