using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>
///     Indicates that the implementing distributed event bus supports event boxes (inbox/outbox).
/// </summary>
public interface ISupportsEventBoxes
{
    /// <summary>
    ///     Publishes an event from the outbox.
    /// </summary>
    /// <param name="outgoingEvent">The outgoing event from the outbox</param>
    /// <param name="outboxConfig">The outbox configuration</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task PublishFromOutboxAsync(
        OutgoingEventInfo outgoingEvent,
        OutboxConfig outboxConfig);

    /// <summary>
    ///     Publishes multiple events from the outbox.
    /// </summary>
    /// <param name="outgoingEvents">The outgoing events from the outbox</param>
    /// <param name="outboxConfig">The outbox configuration</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task PublishManyFromOutboxAsync(
        IEnumerable<OutgoingEventInfo> outgoingEvents,
        OutboxConfig outboxConfig);

    /// <summary>
    ///     Processes an event from the inbox.
    /// </summary>
    /// <param name="incomingEvent">The incoming event from the inbox</param>
    /// <param name="inboxConfig">The inbox configuration</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task ProcessFromInboxAsync(
        IncomingEventInfo incomingEvent,
        InboxConfig inboxConfig);
}
