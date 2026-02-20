namespace WaitingRoom.Projections.Abstractions;

/// <summary>
/// Health snapshot for a projection.
/// </summary>
public sealed record ProjectionHealth
{
    /// <summary>
    /// Indicates whether projection is healthy.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Last time projection processed an event.
    /// </summary>
    public required DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// Current processing lag in milliseconds, if known.
    /// </summary>
    public long? LagMs { get; init; }

    /// <summary>
    /// Optional error message when unhealthy.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
