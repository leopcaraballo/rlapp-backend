namespace WaitingRoom.Projections.Views;

/// <summary>
/// Data transfer object for a patient in the queue.
/// Part of QueueStateView but also useful in other projections.
///
/// Denormalized structure optimized for display.
/// No business logic, pure data.
/// </summary>
public sealed record PatientInQueueDto
{
    /// <summary>
    /// Patient unique identifier.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// Patient full name.
    /// Denormalized from domain event.
    /// </summary>
    public required string PatientName { get; init; }

    /// <summary>
    /// Priority level (high, normal, low).
    /// Used for sorting and monitoring.
    /// </summary>
    public required string Priority { get; init; }

    /// <summary>
    /// When patient checked into queue.
    /// Used for calculating wait time.
    /// </summary>
    public required DateTime CheckInTime { get; init; }

    /// <summary>
    /// Current wait time in minutes.
    /// Calculated projection field (CheckInTime to now).
    /// </summary>
    public required int WaitTimeMinutes { get; init; }
}
