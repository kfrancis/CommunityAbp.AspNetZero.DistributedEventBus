# Changelog

All notable changes to this project are documented here. Versions are produced by MinVer from `v*` git tags.

## Versioning Strategy (v0.11.0+)

Starting with v0.11.0, this project maintains **two independent release lines**:

1. **net10.0 track** — targets **ABP 11.3.x** (AspNetZero 15.4.0+), published to **AspNetZero licensed feed only**.
   - Microsoft.* packages on the .NET 10.0.x line
   - Requires consumer to configure the AspNetZero feed

2. **.NETStandard2.0 track** — targets **ABP 9.0.0** (broadest compatibility), published to **nuget.org**.
   - Microsoft.* packages on the 9.x line
   - Works with AspNetZero 12.x–14.x

**Same NuGet package** (by ID) contains both assets with different dependency floors per TFM. Consumers targeting a given TFM only restore and use the dependencies for that TFM.

---

## v0.11.0

### Packaging & Versioning

- **Dual-track release**: net10.0 now targets ABP 11.3.0 (AspNetZero feed only), while .NETStandard2.0 remains on ABP 9.0.0 (nuget.org).
- **Dependency floors**: net10.0 now declares `Abp >= 11.3.0`; .NETStandard2.0 declares `Abp >= 9.0.0`.
- Microsoft.Extensions.\* 10.0.x for net10.0; 9.x for netstandard2.0.
- Consumers of net10.0 assets MUST configure the AspNetZero NuGet feed (see README).

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

### Unchanged

- `IDistributedEventBus`, `DistributedEventDispatchMode`, the obsolete `useOutbox` overloads,
  `IIncomingMessageDeduplicator`, `IEventTypeRegistry` and `DistributedEventSubscriptionOptions`.

## v0.10.1 and earlier

See the git history and release notes on GitHub.
