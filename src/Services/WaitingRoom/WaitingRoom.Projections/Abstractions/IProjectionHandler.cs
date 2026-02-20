namespace WaitingRoom.Projections.Abstractions;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Handler for processing a specific domain event into projection state.
///
/// Responsibilities:
/// - Extract data from domain event
/// - Perform idempotency check
/// - Update read model state deterministically
/// - Record processing via checkpoint
///
/// Key invariants:
/// - Must be idempotent (same event twice = same state)
/// - Must process events in order
/// - Must be deterministic (no random values, no current time)
/// - Must not contain domain logic
/// - Must not call domain layer
///
/// Concurrency:
/// - Handlers should be stateless and thread-safe
/// - Concurrency controlled by IProjectionContext
/// </summary>
public interface IProjectionHandler
{
    /// <summary>
    /// Gets the event type this handler processes.
    /// Used to route events to correct handler.
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// Handles a domain event idempotently.
    ///
    /// Invariants enforced:
    /// - Same event processed twice produces same state
    /// - Side effects are transactional
    /// - No domain logic, just data transformation
    ///
    /// Pattern:
    /// 1. Check idempotency (already processed?)
    /// 2. If yes, return early
    /// 3. Extract data from event
    /// 4. Update read model(s) deterministically
    /// 5. Mark as processed (idempotency key)
    /// </summary>
    /// <param name="event">Domain event to process.</param>
    /// <param name="context">Projection context for state management.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">If event data invalid.</exception>
    Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default);
}
