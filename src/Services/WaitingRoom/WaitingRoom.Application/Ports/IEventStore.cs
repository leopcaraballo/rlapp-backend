namespace WaitingRoom.Application.Ports;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Aggregates;

/// <summary>
/// Port for Event Store persistence.
///
/// Responsibility:
/// - Retrieve aggregate state from event history
/// - Persist new domain events
/// - Ensure transactional consistency
///
/// This interface isolates Application from Infrastructure.
/// Implementation can be swapped (SQL, Document DB, Event Stream, etc.)
/// without changing Application layer.
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// Retrieves all events for a specific aggregate.
    /// Used to reconstruct aggregate state via event replay.
    /// </summary>
    /// <param name="aggregateId">The aggregate identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All events in order for the aggregate, or empty if not found.</returns>
    Task<IEnumerable<DomainEvent>> GetEventsAsync(
        string aggregateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists uncommitted events from an aggregate.
    /// Must be atomic: all events saved or none.
    ///
    /// Invariants enforced:
    /// - Events saved in order
    /// - Version conflict detection
    /// - Idempotent by idempotencyKey
    /// </summary>
    /// <param name="aggregate">The aggregate with uncommitted events.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="EventConflictException">If version conflict detected.</exception>
    Task SaveAsync(
        WaitingQueue aggregate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an aggregate from its complete event history.
    /// Returns null if aggregate not found.
    /// </summary>
    /// <param name="aggregateId">The aggregate identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reconstructed aggregate or null if not found.</returns>
    Task<WaitingQueue?> LoadAsync(
        string aggregateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all events across all aggregates in deterministic order.
    /// Used for projection rebuilds and event stream processing.
    ///
    /// Invariants:
    /// - Events returned in ascending version order (global clock)
    /// - Same call always returns events in same order
    /// - Used for replay and deterministic projections
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>All events in order by version.</returns>
    Task<IEnumerable<DomainEvent>> GetAllEventsAsync(
        CancellationToken cancellationToken = default);
}
