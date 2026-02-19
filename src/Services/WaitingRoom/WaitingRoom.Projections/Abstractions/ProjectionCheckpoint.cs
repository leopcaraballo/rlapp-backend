namespace WaitingRoom.Projections.Abstractions;

/// <summary>
/// Checkpoint for tracking projection progress.
///
/// Used to:
/// - Track which events have been processed
/// - Resume after interruption
/// - Enforce idempotency (prevent duplicate processing)
/// - Enable incremental updates
///
/// Invariants:
/// - Checkpoints are immutable once created
/// - Version represents last event processed
/// - IdempotencyKey prevents duplicate checkpoint saves
/// </summary>
public sealed record ProjectionCheckpoint
{
    /// <summary>
    /// Unique identifier for this projection.
    /// </summary>
    public required string ProjectionId { get; init; }

    /// <summary>
    /// Last event version successfully processed.
    /// Used to determine which events to process next.
    /// </summary>
    public required long LastEventVersion { get; init; }

    /// <summary>
    /// When this checkpoint was created.
    /// Used for monitoring projection lag.
    /// </summary>
    public required DateTimeOffset CheckpointedAt { get; init; }

    /// <summary>
    /// Unique key for idempotent checkpoint persistence.
    /// Prevents duplicate checkpoint saves on network retry.
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// Optional diagnostic info (e.g., "rebuilding", "catching-up").
    /// </summary>
    public string? Status { get; init; }
}
