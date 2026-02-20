namespace WaitingRoom.Projections.Views;

/// <summary>
/// Detailed view of current queue state.
///
/// Used for:
/// - Detailed monitoring dashboard
/// - Patient list display
/// - Queue management UI
/// - Real-time status checks
///
/// This is a denormalized view — optimized for queries, not normalized.
/// Can be regenerated entirely from domain events.
/// No business logic, pure presentation data.
///
/// Idempotency:
/// - Building same queue twice from events produces identical view
/// - Event replay deterministic
/// - Query always returns current state
/// </summary>
public sealed record QueueStateView
{
    /// <summary>
    /// Queue unique identifier.
    /// </summary>
    public required string QueueId { get; init; }

    /// <summary>
    /// Number of patients currently in queue.
    /// </summary>
    public required int CurrentCount { get; init; }

    /// <summary>
    /// Maximum capacity.
    /// Denormalized from domain state.
    /// </summary>
    public required int MaxCapacity { get; init; }

    /// <summary>
    /// Is queue at max capacity?
    /// Calculated field: CurrentCount >= MaxCapacity.
    /// </summary>
    public required bool IsAtCapacity { get; init; }

    /// <summary>
    /// Remaining spots available.
    /// Calculated: MaxCapacity - CurrentCount.
    /// </summary>
    public required int AvailableSpots { get; init; }

    /// <summary>
    /// Patients currently in queue.
    /// Sorted by priority and check-in time.
    /// </summary>
    public required List<PatientInQueueDto> PatientsInQueue { get; init; }

    /// <summary>
    /// When this view was last updated (projected).
    /// Used for monitoring projection lag.
    /// </summary>
    public required DateTimeOffset ProjectedAt { get; init; }

    /// <summary>
    /// Gets occupancy percentage.
    /// </summary>
    public int OccupancyPercentage => MaxCapacity > 0
        ? (CurrentCount * 100) / MaxCapacity
        : 0;
}
