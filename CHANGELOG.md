# Changelog

All notable changes to this project are documented here. Versions are produced by MinVer from `v*` git tags.

## v0.11.0

### Behavioral changes

- **`AzureServiceBusDistributedEventBus` is now registered as a singleton.** `AzureDistributedEventServiceBusModule`
  replaces `IDistributedEventBus` with `DependencyLifeStyle.Singleton` (previously `Transient`). There is now exactly one
  `ServiceBusClient`, one `ServiceBusSender` and at most one `ServiceBusProcessor` per process. The transient lifetime
  created a client per injection and never disposed it, which leaked AMQP connections until the namespace reported
  `ConnectionsQuotaExceeded`. Hosts that previously overrode the lifetime to Singleton themselves can remove that override.
- **No in-process echo on `Direct` publish for the Azure transport.** `DistributedEventBusBase` gained
  `protected virtual bool DispatchLocallyOnDirectPublish` (default `true`). The Azure bus overrides it to `false`
  because the broker delivers every published message back to the same instance whenever it has a subscription, so
  handlers now fire exactly once, from the broker copy, with real `MessageId`, `EntityPath`, `SubscriptionName` and
  `DeliveryCount` in `IDistributedEventContextAccessor.Current`. Handlers no longer need to filter out in-process
  echoes (for example by skipping contexts with an empty `EntityPath`). The transport-less base bus and in-memory test
  buses still dispatch locally.
- **`Dispose()` releases the transport.** `AzureServiceBusDistributedEventBus.Dispose(bool)` now stops the processor and
  closes the sender and client synchronously with a bounded wait (`ShutdownTimeout`, 10 s by default), logging and
  swallowing shutdown exceptions. `DisposeAsync()` (net10.0) does the same asynchronously. Both are idempotent and safe
  to combine. On netstandard2.0 `Dispose()` fully releases the connection. After disposal `PublishAsync` and `Subscribe`
  throw `ObjectDisposedException`.

### Diagnostics

- Debug logs when the `ServiceBusClient`, `ServiceBusSender` and `ServiceBusProcessor` are created and disposed,
  including a per-instance id so a host can prove there is one bus per process.
- Processor errors are logged at Warning (previously swallowed silently).
- New tag `distributed_eventbus.local_dispatch = true|false` on the `distributed-eventbus.publish` activity.
- New `AzureServiceBusDistributedEventBus.HasActiveProcessor` property.

### Packaging

- Floating `x.y.*` package versions were replaced with explicit minimums so the published nuspec floors stop drifting
  upward at pack time. net10.0 floors: Abp 10.3.0, Abp.EntityFrameworkCore 10.3.0, Microsoft.EntityFrameworkCore.\*
  10.0.11, Microsoft.Extensions.\* 10.0.11, System.Text.Json 10.0.11, System.Linq.Dynamic.Core 1.7.3,
  Azure.Messaging.ServiceBus 7.20.2. netstandard2.0 stays on Abp 9.0.0 and Microsoft.Extensions.\* 9.0.19.
- CI fails when any `PackageReference` under `src/**` uses a wildcard version, and asserts the packed nuspec floors.

### Unchanged

- `IDistributedEventBus`, `DistributedEventDispatchMode`, the obsolete `useOutbox` overloads,
  `IIncomingMessageDeduplicator`, `IEventTypeRegistry` and `DistributedEventSubscriptionOptions`.

## v0.10.1 and earlier

See the git history and release notes on GitHub.
