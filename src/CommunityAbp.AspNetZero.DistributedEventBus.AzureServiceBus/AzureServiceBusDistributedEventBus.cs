using System;
using System.Text.Json;
using System.Threading.Tasks;
using Abp;
using Abp.Threading;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using System.Threading;
using Abp;
using Abp.Threading;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
{
#if NETSTANDARD2_0
    public class AzureServiceBusDistributedEventBus : DistributedEventBusBase
#else
    public class AzureServiceBusDistributedEventBus : DistributedEventBusBase, IAsyncDisposable
#endif
    {
        private readonly ServiceBusClient _client;
        private readonly IAzureServiceBusOptions _options;
        private readonly IEventInbox? _inbox;

        public AzureServiceBusDistributedEventBus(IAzureServiceBusOptions options, IEventInbox? inbox = null)
        {
            _options = options;
            _client = new ServiceBusClient(options.ConnectionString);
            _inbox = inbox; // optional if not configured
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
            if (string.IsNullOrWhiteSpace(_options.SubscriptionName))
            {
                throw new AbpException("Azure Service Bus subscription name is not configured.");
            }

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
                    if (_inbox != null)
                    {
                        var incoming = new IncomingEventInfo(
                            Guid.NewGuid(),
                            args.Message.MessageId,
                            typeof(TEvent).AssemblyQualifiedName!,
                            args.Message.Body.ToArray(),
                            DateTime.UtcNow);
                        await _inbox.AddAsync(incoming, CancellationToken.None);
                    }
                    await handler.HandleEventAsync(data);
                }
                await args.CompleteMessageAsync(args.Message);
            }

            Task ErrorHandler(ProcessErrorEventArgs _) => Task.CompletedTask;

            processor.ProcessMessageAsync += ProcessMessage;
            processor.ProcessErrorAsync += ErrorHandler;

            try
            {
                // Start synchronously to surface startup exceptions immediately
                processor.StartProcessingAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Cleanup if start failed
                processor.ProcessMessageAsync -= ProcessMessage;
                processor.ProcessErrorAsync -= ErrorHandler;
                processor.DisposeAsync().AsTask().GetAwaiter().GetResult();
                throw;
            }

            var baseSubscription = base.Subscribe(handler);

            return new DisposeAction(() =>
            {
                baseSubscription.Dispose();
                AsyncHelper.RunSync(async () =>
                {
                    try
                    {
                        await processor.StopProcessingAsync();
                    }
                    finally
                    {
                        await processor.DisposeAsync();
                    }
                });
            });
        }

#if !NETSTANDARD2_0
        public async ValueTask DisposeAsync()
        {
            await _client.DisposeAsync();
            Dispose();
        }
#endif
    }
}
