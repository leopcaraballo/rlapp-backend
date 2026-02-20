namespace WaitingRoom.Projections.Abstractions;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Port for projection state management and idempotency tracking.
///
/// Responsibilities:
/// - Provide transactional updates to read models
/// - Track processed events (idempotency keys)
/// - Manage checkpoints
/// - Clear state for rebuilds
///
/// All operations within a context should be atomic:
/// - Either all succeed or all rollback
/// - No partial state
///
/// Implementation provided by Infrastructure layer.
/// </summary>
public interface IProjectionContext
{
    /// <summary>
    /// Checks if an event has already been processed.
    /// Used for idempotency: if key exists, skip processing.
    /// </summary>
    /// <param name="idempotencyKey">Unique key for this event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if event already processed.</returns>
    Task<bool> AlreadyProcessedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that an event has been processed.
    /// Prevents duplicate processing on retry.
    /// </summary>
    /// <param name="idempotencyKey">Unique key for this event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkProcessedAsync(
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets current projection checkpoint.
    /// Returns null if projection not yet created.
    /// </summary>
    /// <param name="projectionId">Projection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current checkpoint or null.</returns>
    Task<ProjectionCheckpoint?> GetCheckpointAsync(
        string projectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves checkpoint after event processing.
    /// Used to track progress and resume after interruption.
    /// </summary>
    /// <param name="checkpoint">Checkpoint to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveCheckpointAsync(
        ProjectionCheckpoint checkpoint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all projection state and idempotency keys.
    /// Used during full rebuild.
    /// </summary>
    /// <param name="projectionId">Projection to clear.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearAsync(
        string projectionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets transaction scope for atomic updates.
    /// Ensures all-or-nothing semantics.
    /// </summary>
    /// <returns>Transaction scope (should be used with using statement).</returns>
    IAsyncDisposable BeginTransactionAsync();
}
