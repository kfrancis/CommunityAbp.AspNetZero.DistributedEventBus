using System;
using System.Text.Json;
using System.Threading.Tasks;
using Abp;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;

public class AzureServiceBusDistributedEventBus : DistributedEventBusBase
{
    private readonly ServiceBusClient _client;
    private readonly IAzureServiceBusOptions _options;

    public AzureServiceBusDistributedEventBus(IAzureServiceBusOptions options)
    {
        _options = options;
        _client = new ServiceBusClient(options.ConnectionString);
    }

    public override async Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
    {
        await base.PublishAsync(eventData, onUnitOfWorkComplete, useOutbox);

        var sender = _client.CreateSender(_options.EntityPath);
        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(eventData))
        {
            Subject = typeof(TEvent).FullName
        };

        await sender.SendMessageAsync(message);
    }

    public override IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler)
    {
        var processor = _client.CreateProcessor(_options.EntityPath, _options.SubscriptionName);

        async Task ProcessMessage(ProcessMessageEventArgs args)
        {
            if (args.Message.Subject != typeof(TEvent).FullName)
            {
                return;
            }

            var data = JsonSerializer.Deserialize<TEvent>(args.Message.Body);
            if (data != null)
            {
                await handler.HandleEventAsync(data);
            }
            await args.CompleteMessageAsync(args.Message);
        }

        Task ErrorHandler(ProcessErrorEventArgs args)
        {
            // In real implementation we should log the exception
            return Task.CompletedTask;
        }

        processor.ProcessMessageAsync += ProcessMessage;
        processor.ProcessErrorAsync += ErrorHandler;
        processor.StartProcessingAsync();

        var dispose = new DisposeAction(async () =>
        {
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        });

        // Also register handler locally
        base.Subscribe(handler);

        return dispose;
    }
}
