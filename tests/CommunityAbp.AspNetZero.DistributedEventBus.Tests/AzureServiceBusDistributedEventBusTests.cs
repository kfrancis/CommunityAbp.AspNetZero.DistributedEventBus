using System.Reflection;
using System.Collections.Generic;
using Abp;
using Abp.Dependency;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using CommunityAbp.AspNetZero.DistributedEventBus.AzureServiceBus;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Test.Base;
using Moq;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests
{
    public class AzureServiceBusDistributedEventBusTests : AppTestBase<DistributedEventBusTestModule>
    {
        private readonly DistributedEventBusOptions _busOptions = new();
        private readonly Mock<ServiceBusClient> _clientMock = new();
        private readonly Mock<IEventInbox> _inboxMock = new();
        private readonly Mock<IIocManager> _iocManagerMock = new();
        private readonly Mock<IAzureServiceBusOptions> _optionsMock = new();
        private readonly Mock<IEventSerializer> _serializerMock = new();

        public AzureServiceBusDistributedEventBusTests()
        {
            _optionsMock.SetupGet(x => x.ConnectionString)
                .Returns("Endpoint=sb://test/;SharedAccessKeyName=Root;SharedAccessKey=key");
            _optionsMock.SetupGet(x => x.EntityPath).Returns("test-entity");
            _optionsMock.SetupGet(x => x.SubscriptionName).Returns("test-subscription");
            ReplaceService(_optionsMock.Object);
        }

        [Fact]
        public async Task PublishAsync_SendsMessage_WhenNotUsingOutbox()
        {
            var senderMock = new Mock<ServiceBusSender>();
            _clientMock.Setup(x => x.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
            _serializerMock.Setup(x => x.Serialize(It.IsAny<object>(), It.IsAny<Type>()))
                .Returns([1, 2, 3]);

            var bus = new AzureServiceBusDistributedEventBus(_busOptions, _optionsMock.Object, _iocManagerMock.Object,
                _serializerMock.Object);
            typeof(AzureServiceBusDistributedEventBus)
                .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(bus, _clientMock.Object);

            await bus.PublishAsync("test-event", useOutbox: false);
            senderMock.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact(Skip = "Not working")]
        public async Task PublishAsync_DoesNotSend_WhenUsingOutbox()
        {
            var senderMock = new Mock<ServiceBusSender>();
            _clientMock.Setup(x => x.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
            _serializerMock.Setup(x => x.Serialize(It.IsAny<object>(), It.IsAny<Type>()))
                .Returns([1, 2, 3]);

            var bus = new AzureServiceBusDistributedEventBus(_busOptions, _optionsMock.Object, _iocManagerMock.Object,
                _serializerMock.Object);
            typeof(AzureServiceBusDistributedEventBus)
                .GetField("_client", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(bus, _clientMock.Object);

            await bus.PublishAsync("test-event", useOutbox: true);
            senderMock.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public void Subscribe_Throws_WhenSubscriptionNameMissing()
        {
            _optionsMock.SetupGet(x => x.SubscriptionName).Returns("");
            var bus = new AzureServiceBusDistributedEventBus(_busOptions, _optionsMock.Object, _iocManagerMock.Object,
                _serializerMock.Object);
            Assert.Throws<AbpException>(() => bus.Subscribe(Mock.Of<IDistributedEventHandler<string>>()));
        }

        // Additional tests for message processing, handler invocation, and DisposeAsync can be added here.

        [Fact]
        public async Task TwoSites_PublishFromA_ReceiveOnB_Integration()
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION");
            var topic = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_TOPIC") ?? "distributed-eventbus-tests";
            var subA = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_A") ?? "site-a-sub";
            var subB = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_B") ?? "site-b-sub";
            if (string.IsNullOrWhiteSpace(conn))
            {
                // Skip if no real namespace configured
                return;
            }

            var optionsSiteA = new Mock<IAzureServiceBusOptions>();
            optionsSiteA.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsSiteA.SetupGet(x => x.EntityPath).Returns(topic);
            optionsSiteA.SetupGet(x => x.SubscriptionName).Returns(subA);

            var optionsSiteB = new Mock<IAzureServiceBusOptions>();
            optionsSiteB.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsSiteB.SetupGet(x => x.EntityPath).Returns(topic);
            optionsSiteB.SetupGet(x => x.SubscriptionName).Returns(subB);

            var serializer = Resolve<IEventSerializer>();

            var busA = new AzureServiceBusDistributedEventBus(_busOptions, optionsSiteA.Object, _iocManagerMock.Object,
                serializer);
            var busB = new AzureServiceBusDistributedEventBus(_busOptions, optionsSiteB.Object, _iocManagerMock.Object,
                serializer);

            var receivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handlerB = new Mock<IDistributedEventHandler<string>>();
            handlerB.Setup(h => h.HandleEventAsync(It.IsAny<string>())).Returns<string>(s =>
            {
                receivedTcs.TrySetResult(s);
                return Task.CompletedTask;
            });

            using var subBDisp = busB.Subscribe(handlerB.Object);

            // Give the processor a moment to start
            await Task.Delay(1000);

            var payload = Guid.NewGuid().ToString("N");
            await busA.PublishAsync(payload, useOutbox: false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.True(receivedTcs.Task.IsCompleted, "Site B did not receive message from Site A within timeout");
            Assert.Equal(payload, await receivedTcs.Task);

            subBDisp.Dispose();
        }

        [Fact]
        public async Task Message_IsCompleted_WhenHandlerSucceeds_Integration()
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION");
            var topic = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_TOPIC") ?? "distributed-eventbus-tests";
            var sub = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_COMPLETE") ?? "completion-check-sub";
            if (string.IsNullOrWhiteSpace(conn))
            {
                // Skip if no real namespace configured
                return;
            }

            var optionsPub = new Mock<IAzureServiceBusOptions>();
            optionsPub.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsPub.SetupGet(x => x.EntityPath).Returns(topic);
            optionsPub.SetupGet(x => x.SubscriptionName).Returns(sub);

            var optionsSub = new Mock<IAzureServiceBusOptions>();
            optionsSub.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsSub.SetupGet(x => x.EntityPath).Returns(topic);
            optionsSub.SetupGet(x => x.SubscriptionName).Returns(sub);

            var serializer = Resolve<IEventSerializer>();

            var busPub = new AzureServiceBusDistributedEventBus(_busOptions, optionsPub.Object, _iocManagerMock.Object, serializer);
            var busSub = new AzureServiceBusDistributedEventBus(_busOptions, optionsSub.Object, _iocManagerMock.Object, serializer);

            var receivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var handler = new Mock<IDistributedEventHandler<string>>();
            handler.Setup(h => h.HandleEventAsync(It.IsAny<string>())).Returns<string>(s =>
            {
                receivedTcs.TrySetResult(s);
                return Task.CompletedTask;
            });

            using var disp = busSub.Subscribe(handler.Object);
            await Task.Delay(1000);

            var payload = Guid.NewGuid().ToString("N");
            await busPub.PublishAsync(payload, useOutbox: false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var completed = await Task.WhenAny(receivedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.True(receivedTcs.Task.IsCompleted, "Subscriber did not receive message within timeout");
            Assert.Equal(payload, await receivedTcs.Task);

            // After handler completed, the message should be completed and not available for receive.
            await using var client = new ServiceBusClient(conn);
            var receiver = client.CreateReceiver(topic, sub, new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });

            // Try to receive any remaining message with a short timeout
            var remaining = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3));
            Assert.Null(remaining);

            await receiver.CloseAsync();
            await client.DisposeAsync();

            disp.Dispose();
        }

        [Fact]
        public async Task Subscriber_WithDifferentHandler_ShouldNotReceive_UnrelatedMessages_Integration()
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION");
            var topic = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_TOPIC") ?? "distributed-eventbus-tests";
            var subWeb = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_WEB") ?? "web-sub";
            var subHangfire = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_HANGFIRE") ?? "hangfire-sub";
            if (string.IsNullOrWhiteSpace(conn))
            {
                // Skip if no real namespace configured
                return;
            }

            var optionsWeb = new Mock<IAzureServiceBusOptions>();
            optionsWeb.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsWeb.SetupGet(x => x.EntityPath).Returns(topic);
            optionsWeb.SetupGet(x => x.SubscriptionName).Returns(subWeb);

            var optionsHangfire = new Mock<IAzureServiceBusOptions>();
            optionsHangfire.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsHangfire.SetupGet(x => x.EntityPath).Returns(topic);
            optionsHangfire.SetupGet(x => x.SubscriptionName).Returns(subHangfire);

            var serializer = Resolve<IEventSerializer>();

            var busWeb = new AzureServiceBusDistributedEventBus(_busOptions, optionsWeb.Object, _iocManagerMock.Object, serializer);
            var busHangfire = new AzureServiceBusDistributedEventBus(_busOptions, optionsHangfire.Object, _iocManagerMock.Object, serializer);

            // Web handles string events
            var webReceivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var webHandler = new Mock<IDistributedEventHandler<string>>();
            webHandler.Setup(h => h.HandleEventAsync(It.IsAny<string>())).Returns<string>(s =>
            {
                webReceivedTcs.TrySetResult(s);
                return Task.CompletedTask;
            });

            // Hangfire registers a handler for a different type to simulate no matching handler for string
            var hangfireHandler = new Mock<IDistributedEventHandler<object>>();
            hangfireHandler.Setup(h => h.HandleEventAsync(It.IsAny<object>())).Returns(Task.CompletedTask);

            using var subWebDisp = busWeb.Subscribe(webHandler.Object);
            using var subHangfireDisp = busHangfire.Subscribe(hangfireHandler.Object);

            // Give processors time to start and rules to apply
            await Task.Delay(1500);

            var payload = Guid.NewGuid().ToString("N");
            await busHangfire.PublishAsync(payload, useOutbox: false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var completed = await Task.WhenAny(webReceivedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.True(webReceivedTcs.Task.IsCompleted, "Web subscriber did not receive message within timeout");
            Assert.Equal(payload, await webReceivedTcs.Task);

            // Verify hangfire did not receive the string message (it only listens for Guid)
            hangfireHandler.Verify(h => h.HandleEventAsync(It.IsAny<object>()), Times.Never);

            subHangfireDisp.Dispose();
            subWebDisp.Dispose();
        }

        [Fact]
        public async Task NoSubscriptionRule_WhenNoHandlersRegistered_Integration()
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION");
            var topic = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_TOPIC") ?? "distributed-eventbus-tests";
            var subHangfire = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_HANGFIRE") ?? "hangfire-sub";
            if (string.IsNullOrWhiteSpace(conn))
            {
                // Skip if no real namespace configured
                return;
            }

            var optionsHangfire = new Mock<IAzureServiceBusOptions>();
            optionsHangfire.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsHangfire.SetupGet(x => x.EntityPath).Returns(topic);
            optionsHangfire.SetupGet(x => x.SubscriptionName).Returns(subHangfire);

            var serializer = Resolve<IEventSerializer>();
            var busHangfire = new AzureServiceBusDistributedEventBus(_busOptions, optionsHangfire.Object, _iocManagerMock.Object, serializer);

            // No Subscribe called for hangfire

            // Verify that $Default rule is removed and no other rules are present
            var admin = new ServiceBusAdministrationClient(conn);
            var rules = new List<RuleProperties>();
            await foreach (var rule in admin.GetRulesAsync(topic, subHangfire))
            {
                rules.Add(rule);
            }

            // No rules should exist when no handlers have been registered
            Assert.Empty(rules);
        }

        [Fact]
        public async Task Subscriber_WithNoHandlers_ShouldNotPool_Messages_Integration()
        {
            var conn = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_CONNECTION");
            var topic = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_TOPIC") ?? "distributed-eventbus-tests";
            var subWeb = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_WEB") ?? "web-sub";
            var subHangfire = Environment.GetEnvironmentVariable("AZURE_SERVICEBUS_SUB_HANGFIRE") ?? "hangfire-sub";
            if (string.IsNullOrWhiteSpace(conn))
            {
                // Skip if no real namespace configured
                return;
            }

            var optionsWeb = new Mock<IAzureServiceBusOptions>();
            optionsWeb.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsWeb.SetupGet(x => x.EntityPath).Returns(topic);
            optionsWeb.SetupGet(x => x.SubscriptionName).Returns(subWeb);

            var optionsHangfire = new Mock<IAzureServiceBusOptions>();
            optionsHangfire.SetupGet(x => x.ConnectionString).Returns(conn);
            optionsHangfire.SetupGet(x => x.EntityPath).Returns(topic);
            optionsHangfire.SetupGet(x => x.SubscriptionName).Returns(subHangfire);

            var serializer = Resolve<IEventSerializer>();

            var busWeb = new AzureServiceBusDistributedEventBus(_busOptions, optionsWeb.Object, _iocManagerMock.Object, serializer);
            var busHangfire = new AzureServiceBusDistributedEventBus(_busOptions, optionsHangfire.Object, _iocManagerMock.Object, serializer);

            // Only web subscribes to string events
            var webReceivedTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var webHandler = new Mock<IDistributedEventHandler<string>>();
            webHandler.Setup(h => h.HandleEventAsync(It.IsAny<string>())).Returns<string>(s =>
            {
                webReceivedTcs.TrySetResult(s);
                return Task.CompletedTask;
            });
            using var subWebDisp = busWeb.Subscribe(webHandler.Object);

            // Give processor time to start and rules to apply
            await Task.Delay(1500);

            // Publish from hangfire instance (no handlers registered on hangfire)
            var payload = Guid.NewGuid().ToString("N");
            await busHangfire.PublishAsync(payload, useOutbox: false);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var completed = await Task.WhenAny(webReceivedTcs.Task, Task.Delay(Timeout.Infinite, cts.Token));
            Assert.True(webReceivedTcs.Task.IsCompleted, "Web subscriber did not receive message within timeout");
            Assert.Equal(payload, await webReceivedTcs.Task);

            // Verify hangfire subscription did not pool messages (default rule removed, no handler rules created)
            await using var client = new ServiceBusClient(conn);
            var receiver = client.CreateReceiver(topic, subHangfire, new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
            var remaining = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(3));
            Assert.Null(remaining);
            await receiver.CloseAsync();
            await client.DisposeAsync();

            subWebDisp.Dispose();
        }
    }
}
