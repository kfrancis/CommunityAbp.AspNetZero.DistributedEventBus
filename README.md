# CommunityAbp.DistributedEventBus

Reliable distributed domain events for ABP / AspNet Zero using the Inbox / Outbox pattern and (optionally) Azure Service Bus.

Targets:
- .NET Standard 2.0 (ABP 9.0.0, broad compatibility)
- .NET 10 (ABP 11.3.x, modern runtime)

---
## ⚠️ IMPORTANT: AspNetZero Licensed NuGet Feed Required

**Consumers of these packages MUST add the AspNetZero licensed NuGet feed.** ABP 11.3.x packages are published only to the AspNetZero licensed feed (`https://nuget.aspnetzero.com`), not to nuget.org.

### Setup (local / CI)
**Local development:**
```
dotnet nuget add source "https://nuget.aspnetzero.com/<key>/v3/index.json" -n aspnetzero
```

**GitHub Actions CI:** Add repository secret `ASPNETZERO_NUGET_URL` with the feed URL + key, then include:
```yaml
- name: Add AspNetZero NuGet source
  run: dotnet nuget add source "${{ secrets.ASPNETZERO_NUGET_URL }}" -n aspnetzero
```

---
## IMPORTANT STATUS WARNING
> The EntityFrameworkCore persistence module (`CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore`) is experimental. Outbox sending and inbox processing are covered by integration tests, but production durability still requires application-owned retry, monitoring, and deployment validation.
>
> Until completed:
> - Treat Outbox/Inboxes as experimental.
> - Prefer `DistributedEventDispatchMode.Direct` for user-facing progress and completion flows.
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

> NOTE: `DistributedEventDispatchMode.Outbox` persists an event without immediately sending it. A configured `IOutboxSender` must later publish it.

---
## Architecture Overview
```
Publisher (Hangfire / API / etc.)
 => IDistributedEventBus.PublishAsync(event, dispatchMode)
 (Outbox) -> Persist to Outbox(s) -> Outbox Sender -> Broker (Azure Service Bus)
 (Direct) -> Immediate Dispatch -> (local handlers if any) + Direct Broker send (Azure impl)

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
await _bus.PublishAsync(new UserNotificationEvent { UserId = id, Message = "Welcome" }, DistributedEventDispatchMode.Direct);
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
await _bus.PublishAsync(new OrderCreatedEvent { OrderId = order.Id, Total = order.Total }, DistributedEventDispatchMode.Direct);
```

### Manual Subscription
```
_bus.Subscribe(new OrderCreatedHandler());
```

Unsubscribe via the returned `IDisposable`.

---
## Azure Service Bus Specifics
- Requires valid `ConnectionString` + `EntityPath`; use `EntityKind = Topic` with `SubscriptionName` for topics, or `EntityKind = Queue` without a subscription for queues.
- Messages carry a stable `[EventName]` logical identifier plus a legacy CLR identifier for rolling-deploy compatibility.
- If an inbox (`IEventInbox`) is injected into the Azure bus, it will persist incoming messages before handler invocation (experimental when using EF module).
- Queue mode: set `EntityPath` to queue name and omit `SubscriptionName` (adjust the processor code if needed).
- **Lifetime (v0.11.0+):** the bus is registered as a singleton: one `ServiceBusClient`, one `ServiceBusSender`, at most one `ServiceBusProcessor` per process. Do not override the lifetime. `Dispose()` / `DisposeAsync()` release the connection and are idempotent.
- **No in-process echo (v0.11.0+):** a `Direct` publish from the Azure bus does not invoke local handlers; the broker copy does, exactly once, with `MessageId`, `EntityPath`, `SubscriptionName` and `DeliveryCount` in `IDistributedEventContextAccessor.Current`. Handlers do not need to filter out echoes. See [docs/singleton-bus-and-local-dispatch.md](docs/singleton-bus-and-local-dispatch.md).

### Idempotency & Duplicates
Use Inbox storage (experimental), or register `IIncomingMessageDeduplicator` for application-owned duplicate suppression keyed by broker `MessageId`.

---
## Testing
Manual subscription pattern:
```
var bus = Resolve<IDistributedEventBus>();
var handled = false;
var sub = bus.Subscribe(new OrderCreatedHandler(() => handled = true));
await bus.PublishAsync(new OrderCreatedEvent { OrderId = Guid.NewGuid() }, DistributedEventDispatchMode.Direct);
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
`[EventName]` provides stable logical names; fallback is `FullName`. Register public contract types and legacy aliases in every consumer during a rolling deployment. See [MendMD migration](docs/mendmd-migration.md).

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
