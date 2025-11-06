using System.Threading.Tasks;
using Abp.Events.Bus.Handlers;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces
{
    /// <summary>
    ///     Defines the basic contract for a distributed event handler.
    /// </summary>
    /// <typeparam name="TEvent">Event type</typeparam>
    public interface IDistributedEventHandler<in TEvent> : IEventHandler
    {
        /// <summary>
        ///     Handles the distributed event.
        /// </summary>
        /// <param name="eventData">Event data</param>
        /// <returns>A task representing the asynchronous operation</returns>
        Task HandleEventAsync(TEvent eventData);
    }

    // Extension to bridge sync ABP pipeline if needed
    public static class DistributedEventHandlerExtensions
    {
        public static void HandleEvent<TEvent>(this IDistributedEventHandler<TEvent> handler, TEvent eventData)
        {
            Abp.Threading.AsyncHelper.RunSync(() => handler.HandleEventAsync(eventData));
        }
    }
}
