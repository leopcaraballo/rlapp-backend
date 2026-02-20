namespace WaitingRoom.Projections.EventSubscription;

using System;

/// <summary>
/// RabbitMQ event subscriber for projections.
///
/// Responsibilities:
/// - Connect to RabbitMQ topic exchange
/// - Subscribe to WaitingRoom domain events
/// - Deserialize events from JSON
/// - Route to projection handlers
/// - Support graceful shutdown
///
/// Architecture:
/// - Listen for events published by OutboxWorker
/// - Feed events to projection engine
/// - Track processing lag
/// - Handle connection failures gracefully
/// </summary>
public interface IProjectionEventSubscriber : IAsyncDisposable
{
    /// <summary>
    /// Start listening for events on the configured topic.
    /// </summary>
    Task StartAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Stop listening and cleanup resources.
    /// </summary>
    Task StopAsync(CancellationToken cancellation = default);

    /// <summary>
    /// Event raised when an event is received.
    /// </summary>
    event EventHandler<EventReceivedArgs>? EventReceived;

    /// <summary>
    /// Event raised when an error occurs.
    /// </summary>
    event EventHandler<ErrorOccurredArgs>? ErrorOccurred;
}
