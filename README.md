# Distributed Events with Inbox/Outbox Pattern for AspNetZeto

`CommunityAbp.AspNetZero.DistributedEventBus`

## Overview

This project implements the inbox/outbox pattern for reliable event processing in distributed systems using Azure Service Bus as the message broker. The pattern ensures transactional consistency and reliable message delivery between microservices.

## Architecture

![Distributed Events Diagram](./distributed-events-diagram.png)

The system consists of the following components:

- **Service Databases**: Each service maintains its own database
- **Outbox Tables**: Store outgoing events as part of service transactions
- **Inbox Tables**: Store incoming events before processing
- **Azure Service Bus**: Acts as the message broker between services
- **Relay Services**: Background processes that move messages between database tables and Azure Service Bus

## How It Works

1. **Service A** completes a business operation and saves data to its database
2. In the same transaction, it writes an event to its outbox table
3. The **Outbox Relay** periodically polls the outbox for new messages
4. The relay publishes these messages to Azure Service Bus
5. **Service B's** Inbox Relay consumes messages from Azure Service Bus
6. The relay stores these messages in Service B's inbox table
7. Service B processes messages from its inbox table
8. Service B updates its own database based on the event
9. If needed, Service B creates new events in its own outbox

## Benefits

- **Transactional Reliability**: Events are stored atomically with database changes
- **Guaranteed Delivery**: Events aren't lost if Azure Service Bus is temporarily unavailable
- **Exactly-once Processing**: Events can be tracked to prevent duplicates
- **Fault Tolerance**: Failed event processing can be retried from the inbox
- **Scalability**: Services can scale independently

## Azure Service Bus Features Utilized

- **Duplicate Detection**: Prevents processing the same message twice
- **Dead-letter Queues**: Handles failed message processing
- **Scheduled Delivery**: Can delay message processing if needed
- **Sessions**: Can ensure ordered processing of related messages
- **Topics & Subscriptions**: Allows for publish/subscribe scenarios

## Implementation Notes

### Database Schema

The Outbox and Inbox tables should include:

```sql
CREATE TABLE Outbox (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    EventType NVARCHAR(100) NOT NULL,
    EventData NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIME2 NOT NULL,
    ProcessedAt DATETIME2 NULL,
    Status NVARCHAR(20) NOT NULL
);

CREATE TABLE Inbox (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    EventType NVARCHAR(100) NOT NULL,
    EventData NVARCHAR(MAX) NOT NULL,
    ReceivedAt DATETIME2 NOT NULL,
    ProcessedAt DATETIME2 NULL,
    Status NVARCHAR(20) NOT NULL
);
```

### Relay Service Configuration

The Outbox Relay and Inbox Relay services should be configured with:

- Polling interval
- Batch size
- Retry policy
- Error handling logic

## Getting Started

1. Set up Azure Service Bus namespace and queues/topics
2. Create the Inbox and Outbox tables in your service databases
3. Implement the relay services using the provided templates
4. Configure Azure Service Bus connection strings in your services
5. Integrate the outbox pattern into your service's domain operations

## Best Practices

- Use atomic transactions when writing to the database and outbox
- Implement idempotent message handlers to prevent duplicate processing
- Include correlation IDs for tracking event chains across services
- Implement proper error handling and monitoring for relay services
- Consider using a separate Service Bus namespace for critical vs. non-critical events

## Monitoring and Troubleshooting

- Monitor queue depths in Azure Service Bus
- Track message processing latency
- Set up alerts for failed message processing
- Implement logging throughout the event flow
- Use Azure Application Insights for end-to-end tracing

## References

- [Azure Service Bus Documentation](https://docs.microsoft.com/en-us/azure/service-bus-messaging/)
- [Outbox Pattern](https://microservices.io/patterns/data/transactional-outbox.html)
- [Reliable Event Processing in Azure](https://docs.microsoft.com/en-us/azure/architecture/reference-architectures/event-hubs/event-processing)
