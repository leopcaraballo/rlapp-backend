namespace WaitingRoom.Projections.Views;

/// <summary>
/// Monitor view for waiting room dashboard.
///
/// Used for:
/// - High-level operations dashboard
/// - KPI monitoring (wait times, queue depth)
/// - Real-time metrics display
/// - Admin overview
///
/// This view aggregates statistics across a queue.
/// Denormalized for fast queries.
/// Can be rebuilt from domain events.
///
/// Key point:
/// - No domain logic
/// - Purely denormalized metrics
/// - Deterministic replay-based
/// </summary>
public sealed record WaitingRoomMonitorView
{
    /// <summary>
    /// Queue unique identifier.
    /// </summary>
    public required string QueueId { get; init; }

    /// <summary>
    /// Total patients currently waiting.
    /// </summary>
    public required int TotalPatientsWaiting { get; init; }

    /// <summary>
    /// Patients with high priority status.
    /// </summary>
    public required int HighPriorityCount { get; init; }

    /// <summary>
    /// Patients with normal priority status.
    /// </summary>
    public required int NormalPriorityCount { get; init; }

    /// <summary>
    /// Patients with low priority status.
    /// </summary>
    public required int LowPriorityCount { get; init; }

    /// <summary>
    /// When the last patient checked in.
    /// Null if no patients in queue.
    /// Used to calculate queue activity.
    /// </summary>
    public DateTime? LastPatientCheckedInAt { get; init; }

    /// <summary>
    /// Average wait time in minutes.
    /// Calculated from all patients in queue.
    /// </summary>
    public required int AverageWaitTimeMinutes { get; init; }

    /// <summary>
    /// Queue utilization percentage.
    /// Will be calculated in query projection.
    /// </summary>
    public required int UtilizationPercentage { get; init; }

    /// <summary>
    /// When this view was last updated.
    /// Used for monitoring projection staleness.
    /// </summary>
    public required DateTimeOffset ProjectedAt { get; init; }

    /// <summary>
    /// Gets priority distribution.
    /// </summary>
    public string PriorityDistribution =>
        $"High:{HighPriorityCount} Normal:{NormalPriorityCount} Low:{LowPriorityCount}";

    /// <summary>
    /// Is queue experiencing high load?
    /// </summary>
    public bool IsHighLoad => TotalPatientsWaiting > 50;

    /// <summary>
    /// Is queue empty?
    /// </summary>
    public bool IsEmpty => TotalPatientsWaiting == 0;
}
