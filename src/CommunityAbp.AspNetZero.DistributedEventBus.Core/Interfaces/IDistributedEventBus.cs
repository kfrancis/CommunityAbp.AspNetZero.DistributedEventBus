using System;
using System.Threading;
using System.Threading.Tasks;
using Abp.Events.Bus;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;

/// <summary>
///     Defines an interface for distributed event bus.
/// </summary>
public interface IDistributedEventBus : IEventBus, IDisposable
{
    /// <summary>
    ///     Publishes an event using distributed event bus.
    /// </summary>
    /// <typeparam name="TEvent">Event type</typeparam>
    /// <param name="eventData">Event data</param>
    /// <param name="onUnitOfWorkComplete">
    ///     True to publish the event at the end of the current unit of work, false to publish
    ///     immediately
    /// </param>
    /// <param name="useOutbox">True to use outbox pattern, false to publish directly</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task PublishAsync<TEvent>(
        TEvent eventData,
        bool onUnitOfWorkComplete = true,
        bool useOutbox = true)
        where TEvent : class;

    /// <summary>
    ///     Publishes an event using distributed event bus.
    /// </summary>
    /// <param name="eventType">Event type</param>
    /// <param name="eventData">Event data</param>
    /// <param name="onUnitOfWorkComplete">
    ///     True to publish the event at the end of the current unit of work, false to publish
    ///     immediately
    /// </param>
    /// <param name="useOutbox">True to use outbox pattern, false to publish directly</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task PublishAsync(
        Type eventType,
        object eventData,
        bool onUnitOfWorkComplete = true,
        bool useOutbox = true);

    /// <summary>
    /// Publish with cancellation token support.
    /// </summary>
    Task PublishAsync<TEvent>(
        TEvent eventData,
        CancellationToken cancellationToken,
        bool onUnitOfWorkComplete = true,
        bool useOutbox = true)
        where TEvent : class;

    /// <summary>
    /// Publish (type based) with cancellation token support.
    /// </summary>
    Task PublishAsync(
        Type eventType,
        object eventData,
        CancellationToken cancellationToken,
        bool onUnitOfWorkComplete = true,
        bool useOutbox = true);

    /// <summary>
    ///     Subscribes to an event.
    /// </summary>
    /// <typeparam name="TEvent">Event type</typeparam>
    /// <param name="handler">The handler that will be triggered when the event is published</param>
    /// <returns>Subscription object to dispose to unsubscribe</returns>
    IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler)
        where TEvent : class;

    /// <summary>
    /// Subscribes with (optional) cancellation token (token currently unused but reserved for future async setup).
    /// </summary>
    IDisposable Subscribe<TEvent>(IDistributedEventHandler<TEvent> handler, CancellationToken cancellationToken)
        where TEvent : class;

    /// <summary>
    /// Force initalization of the subscriptions
    /// </summary>
    void InitializeSubscriptions();
}
