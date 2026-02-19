namespace WaitingRoom.Application.DTOs;

/// <summary>
/// DTO representing the state of a waiting queue.
///
/// Used to return queue information from queries.
/// This is a read-only projection of aggregate state.
///
/// Note:
/// - This represents a SNAPSHOT of queue state at a moment
/// - Not part of write model (Event Source)
/// - Can be cached
/// - Derived from events
/// </summary>
public sealed record WaitingQueueDto
{
    /// <summary>
    /// Queue identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable queue name.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// Maximum capacity of this queue.
    /// </summary>
    public required int MaxCapacity { get; init; }

    /// <summary>
    /// Current number of patients in queue.
    /// </summary>
    public required int CurrentCount { get; init; }

    /// <summary>
    /// Queue availability percentage.
    /// </summary>
    public float AvailabilityPercentage =>
        MaxCapacity > 0 ? (1 - (CurrentCount / (float)MaxCapacity)) * 100 : 0;

    /// <summary>
    /// Whether queue is at capacity.
    /// </summary>
    public bool IsAtCapacity => CurrentCount >= MaxCapacity;

    /// <summary>
    /// Aggregate version (event count).
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// When queue was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When queue was last modified.
    /// </summary>
    public required DateTime LastModifiedAt { get; init; }
}
