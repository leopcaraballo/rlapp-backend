namespace WaitingRoom.Projections.Abstractions;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Projection orchestrator that manages event handlers and state.
///
/// Responsibilities:
/// - Register event handlers
/// - Coordinate event processing
/// - Manage projection lifecycle
/// - Handle rebuilds
///
/// A projection is a complete read model derived from domain events.
/// Example: WaitingRoomProjection builds WaitingRoomMonitorView and QueueStateView.
///
/// Key properties:
/// - Idempotent: same events → same final state
/// - Rebuildable: can recreate from events
/// - Observable: tracks progress via checkpoints
/// </summary>
public interface IProjection
{
    /// <summary>
    /// Unique identifier for this projection.
    /// Used in checkpoints and logging.
    /// </summary>
    string ProjectionId { get; }

    /// <summary>
    /// Gets all event handlers registered for this projection.
    /// </summary>
    IReadOnlyList<IProjectionHandler> GetHandlers();

    /// <summary>
    /// Gets current checkpoint (progress tracking).
    /// Returns null if projection never run.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current checkpoint or null.</returns>
    Task<ProjectionCheckpoint?> GetCheckpointAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds projection from scratch.
    /// Clears all state and replays all events from EventStore.
    /// Used for:
    /// - Initial population
    /// - Schema changes
    /// - Disaster recovery
    /// - Verification (compare rebuild with incremental)
    ///
    /// This is a potentially long-running operation.
    /// Should run asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for interruption.</param>
    Task RebuildAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a single event through this projection.
    /// Used by Outbox or event stream subscriber.
    /// </summary>
    /// <param name="event">Domain event to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessEventAsync(
        DomainEvent @event,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes multiple events idempotently.
    /// Used for batch operations and rebuild.
    /// </summary>
    /// <param name="events">Domain events to process.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ProcessEventsAsync(
        IEnumerable<DomainEvent> events,
        CancellationToken cancellationToken = default);
}
