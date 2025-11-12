# CommunityAbp.DistributedEventBus

Reliable distributed domain events for ABP / AspNet Zero using the Inbox / Outbox pattern and (optionally) Azure Service Bus.

Targets:
- .NET Standard2.0 (broad compatibility)
- .NET8 (modern runtime)

---
## IMPORTANT STATUS WARNING
> The EntityFrameworkCore persistence module (`CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore`) is currently INCOMPLETE / NOT PRODUCTION READY. Outbox sending & Inbox processing reliability paths are not finalized. Using `useOutbox: true` will not provide guaranteed store-and-forward semantics. Do NOT rely on the EF module for production durability yet.
>
> Until completed:
> - Treat Outbox/Inboxes as experimental.
> - Prefer direct publish (useOutbox=false) for critical flows.
> - Expect schema/behavior changes.
>
> A compile-time warning is emitted when referencing the EF module.

---
## Key Features
- Distributed event bus abstraction (`IDistributedEventBus`)
- Optional Azure Service Bus transport (`AzureServiceBusDistributedEventBus`)
- Outbox & Inbox persistence abstraction (plug in any storage)
- Configurable multiple Outboxes / Inboxes with filtering predicates
- Polling sender / processor manager abstractions (`IOutboxSender`, `IInboxProcessor`)
- Event boxing interfaces (`ISupportsEventBoxes`) for replay / reliability
- Strongly typed async handlers (`IDistributedEventHandler<TEvent>`)
- Optional stable logical names via `[EventName]` attribute
- Manual, explicit subscription model (no auto-discovery / auto-subscribe)
- Test friendly (replace the bus with an in‑memory implementation)

> NOTE: Current Azure implementation publishes directly to Service Bus even when `useOutbox == true` (it first calls base which may persist to an outbox, then sends immediately). If you intend a strict store‑and‑forward flow, adapt that behavior.

---
## Architecture Overview
```
Publisher (Hangfire / API / etc.)
 => IDistributedEventBus.PublishAsync(event, useOutbox?)
 (useOutbox=true) -> Persist to Outbox(s) -> Outbox Sender -> Broker (Azure Service Bus)
 (useOutbox=false) -> Immediate Dispatch -> (local handlers if any) + Direct Broker send (Azure impl)

Azure Service Bus Topic / Queue
 -> Consumer application subscription
 -> Message received -> (optional Inbox persist) -> Dispatch to manually subscribed handlers
```

No implicit handler scanning: each consumer process decides which handlers to subscribe.

---
## Manual Subscription Model
Auto-subscribe was removed to:
- Avoid resolving optional dependencies (e.g. SignalR hubs) in publisher processes
- Make handler activation explicit & environment-specific

You must subscribe handlers manually at application initialization:
```
public override void OnApplicationInitialization(AbpApplicationInitializationContext ctx)
{
 var bus = IocManager.Resolve<IDistributedEventBus>();
 var hubHandler = IocManager.Resolve<BackgroundJobEventHub>(); // implements IDistributedEventHandler<BackgroundJobEventBase>
 bus.Subscribe<BackgroundJobEventBase>(hubHandler);
}
```

Publisher processes (e.g. Hangfire) only reference event contracts and publish; they do not subscribe SignalR hubs or other consumer-only handlers.

---
## Publisher / Consumer Separation Example

Create a shared contracts library:
```
public class UserNotificationEvent : EventData
{
 public Guid UserId { get; set; }
 public string Message { get; set; } = string.Empty;
}
```

Publisher (Hangfire):
```
await _bus.PublishAsync(new UserNotificationEvent { UserId = id, Message = "Welcome" }, useOutbox: false);
```
(No handler subscription; do NOT reference the SignalR hub assembly.)

Consumer (MVC with SignalR):
```
public class NotificationsHub : Hub, IDistributedEventHandler<UserNotificationEvent>
{
 private readonly IHubContext<NotificationsHub> _context;
 public NotificationsHub(IHubContext<NotificationsHub> context) => _context = context;
 public async Task HandleEventAsync(UserNotificationEvent evt)
 => await _context.Clients.User(evt.UserId.ToString()).SendCoreAsync("notification", new object[] { evt.Message });
}

public class MvcModule : AbpModule
{
 public override void OnApplicationInitialization(AbpApplicationInitializationContext context)
 {
 var bus = IocManager.Resolve<IDistributedEventBus>();
 var hub = IocManager.Resolve<NotificationsHub>();
 bus.Subscribe<UserNotificationEvent>(hub);
 }
}
```

---
## Packages (Current Layout)
- `CommunityAbp.AspNetZero.DistributedEventBus.Core`
- `CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus`
- `CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore` (INCOMPLETE – experimental persistence)
- Test infrastructure in `tests/*`

---
## Installation (Example)
Add packages:
```
dotnet add package CommunityAbp.AspNetZero.DistributedEventBus.Core
# Optional
dotnet add package CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
# Persistence (EF module currently incomplete / experimental)
dotnet add package CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore
```

Register ABP modules (consumer example):
```
[DependsOn(
 typeof(AspNetZeroDistributedEventBusModule),
 typeof(AzureDistributedEventServiceBusModule), // Azure transport
 typeof(AspNetZeroDistributedEventEntityFrameworkCoreModule) // EF persistence (optional / experimental)
)]
public class ConsumerModule : AbpModule
{
 public override void PreInitialize()
 {
 var options = IocManager.Resolve<DistributedEventBusOptions>();
 options.Inboxes.Configure("Default", i =>
 {
 i.ImplementationType = typeof(EfCoreEventInbox);
 i.EventSelector = _ => true;
 i.IsProcessingEnabled = true; // experimental
 });
 }
 public override void OnApplicationInitialization(AbpApplicationInitializationContext ctx)
 {
 var bus = IocManager.Resolve<IDistributedEventBus>();
 var hub = IocManager.Resolve<NotificationsHub>();
 bus.Subscribe<UserNotificationEvent>(hub);
 }
}
```

`appsettings.json` snippet for Azure Service Bus:
```
"AzureServiceBus": {
 "ConnectionString": "Endpoint=sb://<namespace>.servicebus.windows.net/;SharedAccessKeyName=<KeyName>;SharedAccessKey=<Key>",
 "EntityPath": "app-events", // topic or queue
 "SubscriptionName": "consumer-app" // required for topic subscriptions
}
```

---
## Using Your Production DbContext For Migrations (Recommended)
(Same as previous version; unchanged. EF inbox/outbox tables still subject to change.)
1. Reference the EF package.
2. Add DbSets to your DbContext:
```
public DbSet<OutboxMessage> OutboxMessages { get; set; }
public DbSet<InboxMessage> InboxMessages { get; set; }
```
3. Call configuration in `OnModelCreating`:
```
modelBuilder.ConfigureDistributedEventBus();
```
4. Generate & apply migrations from your app's data project.

---
## Production Setup (Summary)
1. Register modules.
2. Configure Outboxes / Inboxes.
3. Add hosted service to start polling (`IOutboxSender`, `IInboxProcessor`).
4. Manually subscribe handlers at startup.
5. Publish events from publishers (no handler dependencies required).

Hosted service sample (unchanged):
```
public class EventBoxesHostedService : IHostedService
{
 private readonly IOutboxSender _outbox;
 private readonly IInboxProcessor _inbox;
 private readonly DistributedEventBusOptions _opts;
 public EventBoxesHostedService(IOutboxSender o, IInboxProcessor i, DistributedEventBusOptions opts)
 { _outbox = o; _inbox = i; _opts = opts; }
 public async Task StartAsync(CancellationToken ct)
 {
 foreach (var ob in _opts.Outboxes.Values.Where(x => x.IsSendingEnabled)) await _outbox.StartAsync(ob);
 foreach (var ib in _opts.Inboxes.Values.Where(x => x.IsProcessingEnabled)) await _inbox.StartAsync(ib);
 }
 public async Task StopAsync(CancellationToken ct)
 { await _outbox.StopAsync(); await _inbox.StopAsync(); }
}
```

---
## Defining & Handling Events
```
[EventName("Orders.Created")]
public class OrderCreatedEvent : EventData { public Guid OrderId { get; set; } public decimal Total { get; set; } }

public class OrderCreatedHandler : IDistributedEventHandler<OrderCreatedEvent>
{
 public Task HandleEventAsync(OrderCreatedEvent evt)
 { /* side effects */ return Task.CompletedTask; }
}
```

### Publishing
```
await _bus.PublishAsync(new OrderCreatedEvent { OrderId = order.Id, Total = order.Total }, useOutbox: true); // experimental reliability path
```

### Manual Subscription
```
_bus.Subscribe(new OrderCreatedHandler());
```

Unsubscribe via the returned `IDisposable`.

---
## Azure Service Bus Specifics
- Requires valid `ConnectionString` + `EntityPath` (+ `SubscriptionName` for topics).
- Messages carry `Subject = typeof(TEvent).FullName` for filtering.
- If an inbox (`IEventInbox`) is injected into the Azure bus, it will persist incoming messages before handler invocation (experimental when using EF module).
- Queue mode: set `EntityPath` to queue name and omit `SubscriptionName` (adjust the processor code if needed).

### Idempotency & Duplicates
Use Inbox storage (experimental); enforce unique `MessageId` or stable event IDs.

---
## Testing
Manual subscription pattern:
```
var bus = Resolve<IDistributedEventBus>();
var handled = false;
var sub = bus.Subscribe(new OrderCreatedHandler(() => handled = true));
await bus.PublishAsync(new OrderCreatedEvent { OrderId = Guid.NewGuid() }, useOutbox: false);
Assert.True(handled);
```

---
## Extension Points
| Interface | Purpose |
|-----------|---------|
| `IEventOutbox` | Persist outgoing events. |
| `IEventInbox` | Persist incoming broker events. |
| `IOutboxSender` | Poll / send pending outbox events. |
| `IInboxProcessor` | Poll / process pending inbox events. |
| `IDistributedEventBus` | Replace transport (RabbitMQ, Kafka, etc.). |

---
## Correlation & Tracing
`OutgoingEventInfo` / `IncomingEventInfo` expose correlation helpers; attach IDs before persistence.

---
## Roadmap / Ideas
- Complete EF persistence reliability path
- Respect `onUnitOfWorkComplete`
- Retry / exponential backoff
- Batch operations
- Instrumentation (ActivitySource)
- Additional transports (RabbitMQ, Kafka, Event Hubs)

---
## Limitations (Current)
- EF persistence incomplete
- Azure bus publishes immediately (even if outbox used)
- No built-in hosted service registration (sample provided)
- Manual subscription required (intentional design)

---
## Event Naming
`[EventName]` provides stable logical names; fallback is `FullName`.

---
## Contributing
1. Fork & clone
2. Create feature branch
3. Add tests
4. Submit PR

---
## License
MIT ©2025 Kori Francis

---
## References
- Azure Service Bus: https://learn.microsoft.com/azure/service-bus-messaging/
- Transactional Outbox Pattern: https://microservices.io/patterns/data/transactional-outbox.html
- ABP Framework: https://abp.io/
