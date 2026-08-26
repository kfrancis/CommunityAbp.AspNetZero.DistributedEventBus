using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Abp;
using Abp.Dependency;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using Microsoft.Extensions.Logging;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;

#if NETSTANDARD2_0
public class AzureServiceBusDistributedEventBus : DistributedEventBusBase
#else
public class AzureServiceBusDistributedEventBus : DistributedEventBusBase, IAsyncDisposable
#endif
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly IAzureServiceBusOptions _options;
    private readonly IEventInbox? _inbox;
    private readonly IEventSerializer _serializer;
    private readonly IIncomingMessageDeduplicator? _deduplicator;
    private readonly ILogger<AzureServiceBusDistributedEventBus>? _logger;
    private readonly object _processorLock = new();
    private readonly ConcurrentDictionary<string, Type> _typesByIdentifier = new(StringComparer.Ordinal);
    private ServiceBusProcessor? _processor;

    public AzureServiceBusDistributedEventBus(
        DistributedEventBusOptions busOptions,
        IAzureServiceBusOptions options,
        IIocManager iocManager,
        IEventSerializer serializer,
        IEventInbox? inbox = null,
        IEventTypeRegistry? eventTypes = null,
        IDistributedEventContextAccessor? contextAccessor = null,
        IIncomingMessageDeduplicator? deduplicator = null,
        ILogger<AzureServiceBusDistributedEventBus>? logger = null)
        : base(busOptions, iocManager, serializer, eventTypes, contextAccessor)
    {
        _options = options;
        _serializer = serializer;
        _inbox = inbox;
        _deduplicator = deduplicator;
        _logger = logger;
        ValidateOptions(options);
        _client = new ServiceBusClient(options.ConnectionString);
        _sender = _client.CreateSender(options.EntityPath);
    }

    public override Task PublishAsync<TEvent>(TEvent eventData, DistributedEventDispatchMode dispatchMode,
        bool onUnitOfWorkComplete = true, CancellationToken cancellationToken = default) where TEvent : class =>
        PublishAsync(typeof(TEvent), eventData ?? throw new ArgumentNullException(nameof(eventData)), dispatchMode, onUnitOfWorkComplete, cancellationToken);

    public override async Task PublishAsync(Type eventType, object eventData, DistributedEventDispatchMode dispatchMode,
        bool onUnitOfWorkComplete = true, CancellationToken cancellationToken = default)
    {
        await base.PublishAsync(eventType, eventData, dispatchMode, onUnitOfWorkComplete, cancellationToken);
        if (dispatchMode == DistributedEventDispatchMode.Outbox) return;
        cancellationToken.ThrowIfCancellationRequested();
        var message = CreateMessage(eventType, eventData);
        using var activity = DistributedEventBusDiagnostics.ActivitySource.StartActivity("distributed-eventbus.transport.send", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "azure_service_bus");
        activity?.SetTag("messaging.destination.name", _options.EntityPath);
        activity?.SetTag("messaging.message.id", message.MessageId);
        await _sender.SendMessageAsync(message, cancellationToken);
    }

    public override IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        RegisterEventType(typeof(TEvent));
        var subscription = base.Subscribe(handler);
        try
        {
            EnsureProcessorStarted();
            return subscription;
        }
        catch
        {
            subscription.Dispose();
            throw;
        }
    }

    private ServiceBusMessage CreateMessage(Type eventType, object eventData)
    {
        var eventName = GetEventName(eventType);
        var message = new ServiceBusMessage(_serializer.Serialize(eventData, eventType))
        {
            Subject = eventName,
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString("N")
        };
        message.ApplicationProperties["EventName"] = eventName;
        message.ApplicationProperties["ClrType"] = _serializer.GetTypeIdentifier(eventType);
        return message;
    }

    private void RegisterEventType(Type eventType)
    {
        var eventName = GetEventName(eventType);
        _typesByIdentifier.TryAdd(eventName, eventType);
        if (!string.IsNullOrWhiteSpace(eventType.AssemblyQualifiedName)) _typesByIdentifier.TryAdd(eventType.AssemblyQualifiedName, eventType);
        if (!string.IsNullOrWhiteSpace(eventType.FullName)) _typesByIdentifier.TryAdd(eventType.FullName, eventType);
    }

    private void EnsureProcessorStarted()
    {
        lock (_processorLock)
        {
            if (_processor is not null) return;
            _processor = GetEntityKind() == AzureServiceBusEntityKind.Queue
                ? _client.CreateProcessor(_options.EntityPath, new ServiceBusProcessorOptions { AutoCompleteMessages = false })
                : _client.CreateProcessor(_options.EntityPath, _options.SubscriptionName!, new ServiceBusProcessorOptions { AutoCompleteMessages = false });
            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;
            try { _processor.StartProcessingAsync().GetAwaiter().GetResult(); }
            catch
            {
                _processor.ProcessMessageAsync -= ProcessMessageAsync;
                _processor.ProcessErrorAsync -= ProcessErrorAsync;
                _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _processor = null;
                throw;
            }
        }
    }

    private static Task ProcessErrorAsync(ProcessErrorEventArgs args) => Task.CompletedTask;

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var eventType = ResolveMessageType(args.Message);
        if (eventType is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", "The consumer cannot resolve the message event type.", args.CancellationToken);
            return;
        }

        var eventName = GetEventName(eventType);
        var context = new DistributedEventMessageContext
        {
            MessageId = args.Message.MessageId ?? args.Message.SequenceNumber.ToString(),
            EventName = eventName,
            LegacyTypeIdentifier = GetStringProperty(args.Message, "ClrType"),
            EntityPath = _options.EntityPath,
            SubscriptionName = _options.SubscriptionName,
            DeliveryCount = args.Message.DeliveryCount,
            CorrelationId = args.Message.CorrelationId,
            DispatchMode = DistributedEventDispatchMode.Direct
        };
        var acquired = false;
        try
        {
            using var activity = DistributedEventBusDiagnostics.ActivitySource.StartActivity("distributed-eventbus.transport.receive", ActivityKind.Consumer);
            activity?.SetTag("messaging.system", "azure_service_bus");
            activity?.SetTag("messaging.destination.name", _options.EntityPath);
            activity?.SetTag("messaging.message.id", context.MessageId);
            activity?.SetTag("messaging.delivery.count", context.DeliveryCount);

            if (_deduplicator is not null)
            {
                var acquisition = await _deduplicator.TryAcquireAsync(context, args.CancellationToken);
                if (acquisition == IncomingMessageDeduplicationResult.AlreadyCompleted)
                {
                    _logger?.LogInformation("Suppressed duplicate distributed event {MessageId} ({EventName}) from {EntityPath}.", context.MessageId, context.EventName, context.EntityPath);
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                    return;
                }
                if (acquisition == IncomingMessageDeduplicationResult.InProgress)
                {
                    _logger?.LogInformation("Deferring distributed event {MessageId} ({EventName}) because another processor owns its duplicate-suppression lease.", context.MessageId, context.EventName);
                    await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
                    return;
                }
                acquired = true;
            }

            var payload = args.Message.Body.ToArray();
            if (_inbox is not null)
            {
                await _inbox.AddAsync(new IncomingEventInfo(Guid.NewGuid(), context.MessageId, eventName, payload, DateTime.UtcNow)
                    .SetCorrelationId(context.CorrelationId ?? string.Empty)
                    .SetMessageContext(context), args.CancellationToken);
            }
            else
            {
                var eventData = _serializer.Deserialize(payload, eventType);
                if (eventData is null)
                {
                    await args.DeadLetterMessageAsync(args.Message, "InvalidEventPayload", "The message payload could not be deserialized.", args.CancellationToken);
                    return;
                }
                using var contextScope = ContextAccessor.Push(context);
                await DispatchLocalAsync(eventType, eventData, args.CancellationToken);
            }

            if (_deduplicator is not null) await _deduplicator.CompleteAsync(context, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (OperationCanceledException) when (args.CancellationToken.IsCancellationRequested)
        {
            if (acquired && _deduplicator is not null) await _deduplicator.AbandonAsync(context, CancellationToken.None);
            throw;
        }
        catch
        {
            if (acquired && _deduplicator is not null) await _deduplicator.AbandonAsync(context, CancellationToken.None);
            _logger?.LogWarning("Abandoning distributed event {MessageId} ({EventName}) after handler failure. Delivery count: {DeliveryCount}.", context.MessageId, context.EventName, context.DeliveryCount);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Type? ResolveMessageType(ServiceBusReceivedMessage message)
    {
        var eventName = GetStringProperty(message, "EventName") ?? message.Subject;
        var legacyType = GetStringProperty(message, "ClrType");
        foreach (var identifier in new[] { eventName, legacyType })
        {
            if (string.IsNullOrWhiteSpace(identifier)) continue;
            if (_typesByIdentifier.TryGetValue(identifier, out var cached)) return cached;
            var resolved = ResolveEventType(identifier);
            if (resolved is not null) { RegisterEventType(resolved); return resolved; }
        }
        return null;
    }

    private static string? GetStringProperty(ServiceBusReceivedMessage message, string key) =>
        message.ApplicationProperties.TryGetValue(key, out var value) ? value as string : null;

    private AzureServiceBusEntityKind GetEntityKind() => (_options as IAzureServiceBusEntityKindOptions)?.EntityKind ??
        (string.IsNullOrWhiteSpace(_options.SubscriptionName) ? AzureServiceBusEntityKind.Queue : AzureServiceBusEntityKind.Topic);

    private static void ValidateOptions(IAzureServiceBusOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString)) throw new AbpException("Azure Service Bus connection string is required.");
        if (string.IsNullOrWhiteSpace(options.EntityPath)) throw new AbpException("Azure Service Bus entity path is required.");
        var configuredKind = (options as IAzureServiceBusEntityKindOptions)?.EntityKind ??
            (string.IsNullOrWhiteSpace(options.SubscriptionName) ? AzureServiceBusEntityKind.Queue : AzureServiceBusEntityKind.Topic);
        if (configuredKind == AzureServiceBusEntityKind.Topic && string.IsNullOrWhiteSpace(options.SubscriptionName))
            throw new AbpException("Azure Service Bus topic mode requires a subscription name.");
        if (configuredKind == AzureServiceBusEntityKind.Queue && !string.IsNullOrWhiteSpace(options.SubscriptionName))
            throw new AbpException("Azure Service Bus queue mode cannot specify a subscription name.");
        foreach (var part in options.ConnectionString.Split(';'))
        {
            var pair = part.Split(new[] { '=' }, 2);
            if (pair.Length == 2 && pair[0].Trim().Equals("EntityPath", StringComparison.OrdinalIgnoreCase) &&
                !pair[1].Trim().Equals(options.EntityPath, StringComparison.OrdinalIgnoreCase))
                throw new AbpException("Azure Service Bus EntityPath must match the EntityPath embedded in the connection string.");
        }
    }

#if !NETSTANDARD2_0
    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync();
            _processor.ProcessMessageAsync -= ProcessMessageAsync;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;
            await _processor.DisposeAsync();
        }
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
        Dispose();
    }
#endif
}
