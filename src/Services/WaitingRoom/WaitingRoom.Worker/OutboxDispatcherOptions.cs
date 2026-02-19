namespace WaitingRoom.Worker;

/// <summary>
/// Configuration options for Outbox Dispatcher Worker.
///
/// Responsibilities:
/// - Control polling interval
/// - Control batch size
/// - Control retry policy
/// - Deterministic configuration
/// </summary>
public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    /// <summary>
    /// Interval between polling attempts in seconds.
    /// Default: 5 seconds
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of messages to process in a single batch.
    /// Default: 100
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of retry attempts before marking as failed permanently.
    /// Default: 5
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// Base delay for exponential backoff in seconds.
    /// Default: 30 seconds
    /// </summary>
    public int BaseRetryDelaySeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry delay in seconds for exponential backoff.
    /// Default: 3600 seconds (1 hour)
    /// </summary>
    public int MaxRetryDelaySeconds { get; set; } = 3600;
}
