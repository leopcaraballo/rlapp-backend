namespace BuildingBlocks.EventSourcing;

/// <summary>
/// Abstraction for accessing current time.
/// Enables deterministic testing and removes system time coupling from Domain.
/// </summary>
public interface IClock
{
    /// <summary>
    /// Gets current UTC time.
    /// </summary>
    DateTime UtcNow { get; }
}
