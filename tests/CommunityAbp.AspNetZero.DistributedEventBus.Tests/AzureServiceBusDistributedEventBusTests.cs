using System.Reflection;
using Abp;
using Abp.Dependency;
using Azure.Messaging.ServiceBus;
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
    }
}
