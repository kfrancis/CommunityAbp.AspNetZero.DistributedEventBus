# Bus lifetime and local dispatch (v0.11.0)

## Why the bus is a singleton

`AzureServiceBusDistributedEventBus` owns a `ServiceBusClient` (one AMQP connection) and a `ServiceBusSender`. Before
v0.11.0 the module registered it as `Transient`, so every job, controller or application service that injected
`IDistributedEventBus` got its own client, and Castle Windsor never called `DisposeAsync` on release. Each instance
leaked a connection until the namespace hit `ConnectionsQuotaExceeded`.

Since v0.11.0 `AzureDistributedEventServiceBusModule` registers the bus with `DependencyLifeStyle.Singleton`:

- one `ServiceBusClient`, one `ServiceBusSender` per process, created in the constructor without opening a connection
  (the SDK connects lazily on first send or on processor start);
- at most one `ServiceBusProcessor`, started by the first `Subscribe` call;
- `Subscribe`, `PublishAsync` and `Dispose` are safe under concurrent calls.

Hosts that previously overrode the lifetime to Singleton themselves can delete that override. Hosts that relied on the
transient lifetime to get isolated instances should construct `AzureServiceBusDistributedEventBus` directly instead.

Enable `Debug` logging for `CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus` to see one
`Created ServiceBusClient ... (bus instance N)` line per process and matching `Disposed ...` lines at shutdown.

## Disposal

- `Dispose()` stops the processor and closes the sender and client synchronously, waiting at most `ShutdownTimeout`
  (10 s, `protected virtual`). Exceptions during shutdown are logged at Warning and swallowed. Castle calls this when the
  container releases the singleton at host shutdown. On netstandard2.0 this is the only path and fully releases the
  connection.
- `DisposeAsync()` (net10.0) does the same asynchronously.
- Both are idempotent and mutually safe: whichever runs first performs the shutdown, the other is a no-op.
- After disposal `PublishAsync` and `Subscribe` throw `ObjectDisposedException`.

## Local dispatch on `Direct` publish

`DistributedEventBusBase.PublishAsync(..., DistributedEventDispatchMode.Direct)` used to invoke in-process handlers
unconditionally, and the Azure override then sent the message to the broker as well. In a host that both publishes and
subscribes on the same entity (MendMD Web.Mvc) every handler therefore fired twice: once locally with a synthetic
`MessageId` and no `EntityPath`, and once when the broker echoed the message back.

`DistributedEventBusBase` now exposes:

```csharp
protected virtual bool DispatchLocallyOnDirectPublish => true;
```

| Bus | Value | Result of a `Direct` publish |
|---|---|---|
| `DistributedEventBusBase` (in-memory / tests) | `true` | handlers run once, locally |
| `AzureServiceBusDistributedEventBus`, host subscribes on the same entity | `false` | handlers run once, from the broker copy, with `MessageId`, `EntityPath`, `SubscriptionName`, `DeliveryCount` populated |
| `AzureServiceBusDistributedEventBus`, publish-only host | `false` | one broker send, no local handlers (there are none) |

The Azure bus returns `false` unconditionally because its sender and processor always target the same entity and it only
has handlers when it has a processor (`Subscribe` starts one). A conditional rule would produce the same outcomes with
more moving parts.

Handlers no longer need to ignore in-process echoes. Code such as
`if (string.IsNullOrEmpty(contextAccessor.Current?.EntityPath)) return;` can be removed.

The `distributed-eventbus.publish` activity carries `distributed_eventbus.local_dispatch = true|false` so traces show
which path a publish took.
