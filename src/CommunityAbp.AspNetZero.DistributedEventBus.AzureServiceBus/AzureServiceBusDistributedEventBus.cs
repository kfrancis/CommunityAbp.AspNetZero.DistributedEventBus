using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Abp;
using Abp.Dependency;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

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
    private readonly object _processorLock = new();
    private readonly ConcurrentDictionary<string, Type> _typesByIdentifier = new(StringComparer.Ordinal);
    private ServiceBusProcessor? _processor;

    public AzureServiceBusDistributedEventBus(
        DistributedEventBusOptions busOptions,
        IAzureServiceBusOptions options,
        IIocManager iocManager,
        IEventSerializer serializer,
        IEventInbox? inbox = null)
        : base(busOptions, iocManager, serializer)
    {
        _options = options;
        _client = new ServiceBusClient(options.ConnectionString);
        _sender = _client.CreateSender(options.EntityPath);
        _inbox = inbox;
        _serializer = serializer;
    }

    public override Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = false)
    {
        return PublishAsync(typeof(TEvent), eventData!, onUnitOfWorkComplete, useOutbox);
    }

    public override async Task PublishAsync(Type eventType, object eventData, bool onUnitOfWorkComplete = true, bool useOutbox = false)
    {
        await base.PublishAsync(eventType, eventData, onUnitOfWorkComplete, useOutbox);
        if (useOutbox)
        {
            return;
        }

        var message = CreateMessage(eventType, eventData);
        await _sender.SendMessageAsync(message);
    }

    public override IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler)
    {
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        if (string.IsNullOrWhiteSpace(_options.SubscriptionName))
        {
            throw new AbpException("Azure Service Bus subscription name is not configured.");
        }

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
        var message = new ServiceBusMessage(_serializer.Serialize(eventData, eventType))
        {
            Subject = eventType.FullName,
            ContentType = "application/json",
            MessageId = Guid.NewGuid().ToString("N")
        };
        message.ApplicationProperties["ClrType"] = _serializer.GetTypeIdentifier(eventType);
        return message;
    }

    private void RegisterEventType(Type eventType)
    {
        if (!string.IsNullOrWhiteSpace(eventType.AssemblyQualifiedName))
        {
            _typesByIdentifier.TryAdd(eventType.AssemblyQualifiedName, eventType);
        }

        if (!string.IsNullOrWhiteSpace(eventType.FullName))
        {
            _typesByIdentifier.TryAdd(eventType.FullName, eventType);
        }
    }

    private void EnsureProcessorStarted()
    {
        lock (_processorLock)
        {
            if (_processor is not null)
            {
                return;
            }

            _processor = _client.CreateProcessor(
                _options.EntityPath,
                _options.SubscriptionName!,
                new ServiceBusProcessorOptions { AutoCompleteMessages = false });
            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += _ => Task.CompletedTask;

            try
            {
                _processor.StartProcessingAsync().GetAwaiter().GetResult();
            }
            catch
            {
                _processor.ProcessMessageAsync -= ProcessMessageAsync;
                _processor.ProcessErrorAsync -= _ => Task.CompletedTask;
                _processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _processor = null;
                throw;
            }
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var eventType = ResolveMessageType(args.Message);
        if (eventType is null)
        {
            await args.DeadLetterMessageAsync(args.Message, "UnknownEventType", "The consumer cannot resolve the message event type.", args.CancellationToken);
            return;
        }

        var payload = args.Message.Body.ToArray();
        if (_inbox is not null)
        {
            await _inbox.AddAsync(new IncomingEventInfo(
                Guid.NewGuid(),
                args.Message.MessageId ?? args.Message.SequenceNumber.ToString(),
                _serializer.GetTypeIdentifier(eventType),
                payload,
                DateTime.UtcNow), args.CancellationToken);
        }
        else
        {
            var eventData = _serializer.Deserialize(payload, eventType);
            if (eventData is null)
            {
                await args.DeadLetterMessageAsync(args.Message, "InvalidEventPayload", "The message payload could not be deserialized.", args.CancellationToken);
                return;
            }

            await DispatchLocalAsync(eventType, eventData, args.CancellationToken);
        }

        await args.CompleteMessageAsync(args.Message, args.CancellationToken);
    }

    private Type? ResolveMessageType(ServiceBusReceivedMessage message)
    {
        var identifier = message.ApplicationProperties.TryGetValue("ClrType", out var typeValue)
            ? typeValue as string
            : message.Subject;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var typeIdentifier = identifier!;
        if (_typesByIdentifier.TryGetValue(typeIdentifier, out var cached))
        {
            return cached;
        }

        var resolved = Type.GetType(typeIdentifier, throwOnError: false);
        if (resolved is not null)
        {
            _typesByIdentifier.TryAdd(typeIdentifier, resolved);
        }

        return resolved;
    }

#if !NETSTANDARD2_0
    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync();
            await _processor.DisposeAsync();
        }

        await _sender.DisposeAsync();
        await _client.DisposeAsync();
        Dispose();
    }
#endif
}
