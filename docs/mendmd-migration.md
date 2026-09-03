# MendMD production migration

## Dispatch mode

Use explicit dispatch modes for background-job notifications:

```csharp
await distributedEventBus.PublishAsync(progressed, DistributedEventDispatchMode.Direct, cancellationToken: cancellationToken);
```

`Direct` is the recommended mode for user-visible progress and completion events. `Outbox` persists first and remains an experimental store-and-forward path; configure and run its poller before selecting it. Existing `useOutbox` overloads remain compatible but are obsolete.

## Stable contracts

Annotate public, cross-process contracts and register them in every host:

```csharp
[EventName("mendmd.background-job.progressed")]
public sealed class BackgroundJobProgressedEvent : BackgroundJobEventBase { }

eventTypes.Register<BackgroundJobProgressedEvent>("PatientManagement.Application.LegacyProgressedEvent");
```

Keep CLR-name aliases until all deployed consumers understand the logical name and every pre-change broker message has expired. The transport continues including the CLR type identifier during this compatibility window.

## Host configuration

Set `EntityKind = Topic` plus `SubscriptionName = "mendmd-web"` for MVC. Set `EntityKind = Queue` and omit `SubscriptionName` for Hangfire queue consumers. Register base handlers through `DistributedEventSubscriptionOptions.Register<TEvent, THandler>()` during module initialization so shutdown disposes subscriptions safely.

Handlers can inject `IDistributedEventContextAccessor` to read the broker message ID and delivery count. Register `IIncomingMessageDeduplicator` only when the application has durable storage and a defined TTL; duplicate UI notifications should use the broker `MessageId` as their key.

## Security and rollout

Keep `AzureServiceBus:ConnectionString` in user secrets, Key Vault, or deployment secret configuration—not checked-in appsettings. Rotate any previously committed live credentials. Deploy consumers with aliases first, then publishers with stable names; verify traces for `distributed-eventbus.*`, message IDs, entity path, and handler duration before retiring aliases.

## v0.11.0

Two consumer-side workarounds become unnecessary and should be removed from both hosts (Web.Mvc and Web.Hangfire):

1. **Remove the Singleton lifetime override.** `AzureDistributedEventServiceBusModule` now registers
   `IDistributedEventBus` as `DependencyLifeStyle.Singleton` itself. Any host module that re-registered the bus
   (for example `Configuration.ReplaceService<IDistributedEventBus, AzureServiceBusDistributedEventBus>(DependencyLifeStyle.Singleton)`)
   can drop that line. One `ServiceBusClient` per process is guaranteed by the package; the transient-per-injection
   leak that produced `ConnectionsQuotaExceeded` is gone.
2. **Remove echo filtering from handlers.** A `Direct` publish no longer dispatches to in-process handlers on the Azure
   bus, so handlers fire exactly once, from the broker copy. Delete guards such as
   `if (string.IsNullOrEmpty(contextAccessor.Current?.EntityPath)) return;`. `EntityPath`, `SubscriptionName`,
   `MessageId` and `DeliveryCount` are always populated inside a handler.

Web.Hangfire (publish-only) keeps one client and one sender and never starts a processor. Web.Mvc (topic
`distributed-events`, subscription `mendmd-web`) keeps one client, one sender and one processor. Enable `Debug` logging
for `CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus` to confirm one `Created ServiceBusClient` line per
process. Details: [singleton-bus-and-local-dispatch.md](singleton-bus-and-local-dispatch.md).
