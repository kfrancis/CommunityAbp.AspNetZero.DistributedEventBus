using System;
using System.Linq;
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
using Azure.Messaging.ServiceBus.Administration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus
{
#if NETSTANDARD2_0
    public class AzureServiceBusDistributedEventBus : DistributedEventBusBase
#else
    public class AzureServiceBusDistributedEventBus : DistributedEventBusBase, IAsyncDisposable
#endif
    {
        private readonly ServiceBusClient _client;
        private readonly ServiceBusAdministrationClient _adminClient;
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
            _adminClient = new ServiceBusAdministrationClient(options.ConnectionString);
            _inbox = inbox; // optional if not configured
            _serializer = serializer;

            // If a subscription is configured, remove the default catch-all rule so that
            // this subscriber doesn't receive messages unless explicit handler rules are added.
            if (!string.IsNullOrWhiteSpace(_options.SubscriptionName))
            {
                TryRemoveDefaultRule(_options.EntityPath, _options.SubscriptionName).GetAwaiter().GetResult();
            }
        }

        public async override Task PublishAsync<TEvent>(TEvent eventData, bool onUnitOfWorkComplete = true, bool useOutbox = false)
        {
            // Persist to outbox or dispatch directly (base logic)
            await base.PublishAsync(eventData, onUnitOfWorkComplete, useOutbox);

            // If using outbox, defer sending until the outbox sender processes it.
            if (useOutbox) return; // defer to outbox sender

            var sender = _client.CreateSender(_options.EntityPath);
            var bytes = _serializer.Serialize(eventData, typeof(TEvent));
            var message = new ServiceBusMessage(bytes)
            {
                Subject = typeof(TEvent).FullName,  // concrete CLR type FullName
                ApplicationProperties =
                {
                    // Add assembly-qualified name for precise resolution across boundaries
                    ["ClrType"] = typeof(TEvent).AssemblyQualifiedName
                }
            };
            await sender.SendMessageAsync(message);
        }

        private async Task TryRemoveDefaultRule(string topic, string subscription)
        {
            try
            {
                var rulesPager = _adminClient.GetRulesAsync(topic, subscription);
                await foreach (var rule in rulesPager)
                {
                    if (string.Equals(rule.Name, "$Default", StringComparison.Ordinal))
                    {
                        await _adminClient.DeleteRuleAsync(topic, subscription, rule.Name);
                        break;
                    }
                }
            }
            catch
            {
                // Ignore admin errors to keep runtime resilient.
            }
        }

        public override IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler)
        {
            if (string.IsNullOrWhiteSpace(_options.SubscriptionName))
            {
                throw new AbpException("Azure Service Bus subscription name is not configured.");
            }


            // Ensure the subscription only receives messages matching this handler type.
            // This avoids polling unrelated messages when a subscriber has no handler for them.
            EnsureSubscriptionRuleForType<TEvent>(_options.EntityPath, _options.SubscriptionName).GetAwaiter().GetResult();

            var processor = _client.CreateProcessor(_options.EntityPath, _options.SubscriptionName);

            Task ErrorHandler(ProcessErrorEventArgs _) => Task.CompletedTask;

            processor.ProcessMessageAsync += ProcessMessage;
            processor.ProcessErrorAsync += ErrorHandler;

            try
            {
                processor.StartProcessingAsync().GetAwaiter().GetResult();
            }
            catch
            {
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

            async Task ProcessMessage(ProcessMessageEventArgs args)
            {
                // Resolve message type:
                Type? messageType = null;
                if (args.Message.ApplicationProperties.TryGetValue("ClrType", out var aqnObj) && aqnObj is string aqnStr)
                {
                    messageType = Type.GetType(aqnStr, throwOnError: false);
                }
                if (messageType == null && !string.IsNullOrWhiteSpace(args.Message.Subject))
                {
                    // Fallback: search by FullName
                    messageType = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a =>
                        {
                            try { return a.GetTypes(); } catch { return []; }
                        })
                        .FirstOrDefault(t => t.FullName == args.Message.Subject);
                }
                if (messageType == null)
                {
                    // Unknown type -> abandon (could dead-letter in production)
                    await args.AbandonMessageAsync(args.Message);
                    return;
                }

                // Base-class / interface handler support: ensure handler TEvent is assignable from concrete message type
                if (!typeof(TEvent).IsAssignableFrom(messageType))
                {
                    // Not for this handler
                    return;
                }

                // Deserialize using the concrete message type then cast to handler type
                var obj = _serializer.Deserialize(args.Message.Body.ToArray(), messageType);
                if (obj is TEvent typed)
                {
                    if (_inbox != null)
                    {
                        var incoming = new IncomingEventInfo(
                            Guid.NewGuid(),
                            args.Message.MessageId,
                            _serializer.GetTypeIdentifier(messageType),
                            args.Message.Body.ToArray(),
                            DateTime.UtcNow);
                        await _inbox.AddAsync(incoming, CancellationToken.None);
                    }
                    await handler.HandleEventAsync(typed);
                }
                await args.CompleteMessageAsync(args.Message);
            }
        }

        private async Task EnsureSubscriptionRuleForType<TEvent>(string topic, string subscription)
        {
            // Remove default catch-all rule to prevent receiving all messages.
            try
            {
                var rulesPager = _adminClient.GetRulesAsync(topic, subscription);
                await foreach (var rule in rulesPager)
                {
                    if (string.Equals(rule.Name, "$Default", StringComparison.Ordinal))
                    {
                        await _adminClient.DeleteRuleAsync(topic, subscription, rule.Name);
                        break;
                    }
                }
            }
            catch
            {
                // Ignore admin errors; processor will still function, but may receive broader messages.
            }

            // Create or ensure a rule that matches this event type.
            var typeFullName = typeof(TEvent).FullName;
            var aqn = typeof(TEvent).AssemblyQualifiedName;
            var ruleName = $"Type-{typeFullName}";
            var filter = new SqlRuleFilter($"(sys.Label = '{typeFullName}') OR (ClrType = '{aqn}')");
            try
            {
                // If rule exists, skip creating; otherwise create.
                await _adminClient.CreateRuleAsync(topic, subscription, new CreateRuleOptions(ruleName, filter));
            }
            catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
            {
                // Rule already exists; nothing to do.
            }
            catch
            {
                // Ignore admin errors to keep runtime resilient.
            }
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
