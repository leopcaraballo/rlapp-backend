namespace WaitingRoom.Application.Ports;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Port for publishing domain events to infrastructure.
///
/// Responsibility:
/// - Publish domain events to message broker or event stream
/// - Enable event-driven communication between bounded contexts
/// - Decouple Application from actual transport mechanism
///
/// Implementation handles:
/// - Message serialization
/// - Broker connectivity
/// - Idempotency guarantees
/// - Backpressure
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes domain events for downstream subscribers.
    /// Events are published in order they were generated.
    ///
    /// Idempotency:
    /// Events contain IdempotencyKey for duplicate detection.
    /// Publishing same event twice should result in single processing.
    /// </summary>
    /// <param name="events">Domain events to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// This method should NOT be called directly by handlers.
    /// It's typically called by Outbox pattern or Event Bus.
    /// See Infrastructure layer implementation.
    /// </remarks>
    Task PublishAsync(
        IEnumerable<DomainEvent> events,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a single domain event.
    /// </summary>
    /// <param name="event">Event to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(
        DomainEvent @event,
        CancellationToken cancellationToken = default);
}
