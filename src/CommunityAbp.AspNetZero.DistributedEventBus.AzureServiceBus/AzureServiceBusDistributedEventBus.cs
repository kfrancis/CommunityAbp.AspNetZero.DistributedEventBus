using System;
using System.Text.Json;
using System.Threading.Tasks;
using Abp;
using Abp.Threading;
using Azure.Messaging.ServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using System.Threading;
using Abp.Dependency;

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
        private readonly IEventSerializer _serializer;

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
            _inbox = inbox; // optional if not configured
            _serializer = serializer;
        }

        public override async Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = true)
        {
            // Persist to outbox or dispatch directly (base logic)
            await base.PublishAsync(eventData, onUnitOfWorkComplete, useOutbox);

            // If using outbox, defer sending until the outbox sender processes it.
            if (useOutbox)
            {
                return; // store & forward later
            }

            var sender = _client.CreateSender(_options.EntityPath);
            var bytes = _serializer.Serialize(eventData!, typeof(TEvent));
            var message = new ServiceBusMessage(bytes)
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

                var data = _serializer.Deserialize(args.Message.Body.ToArray(), typeof(TEvent)) as TEvent;
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
            return new LocalDispose(() =>
            {
                baseSubscription.Dispose();
                AsyncHelper.RunSync(async () =>
                {
                    try { await processor.StopProcessingAsync(); }
                    finally { await processor.DisposeAsync(); }
                });
            });
        }

        private sealed class LocalDispose : IDisposable
        {
            private readonly Action _d; public LocalDispose(Action d) => _d = d; public void Dispose() => _d();
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
