namespace BuildingBlocks.Observability;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Event processing lag metrics.
///
/// Tracks the time taken for an event to:
/// 1. Be created in the domain
/// 2. Be published to the outbox
/// 3. Be dispatched from outbox to broker
/// 4. Be processed by projections
///
/// Used for monitoring event-driven architecture health and identifying bottlenecks.
/// </summary>
public sealed record EventLagMetrics
{
    /// <summary>
    /// Unique event identifier.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Type of event.
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// Aggregate that generated the event.
    /// </summary>
    public required string AggregateId { get; init; }

    /// <summary>
    /// When the event was created in the domain.
    /// </summary>
    public required DateTime EventCreatedAt { get; init; }

    /// <summary>
    /// When the event was published to the outbox.
    /// </summary>
    public DateTime? EventPublishedAt { get; init; }

    /// <summary>
    /// Time to dispatch from outbox to broker (milliseconds).
    /// </summary>
    public int? OutboxDispatchDurationMs { get; init; }

    /// <summary>
    /// When the event was processed by projections.
    /// </summary>
    public DateTime? EventProcessedAt { get; init; }

    /// <summary>
    /// Time to process by projections after publication (milliseconds).
    /// </summary>
    public int? ProjectionProcessingDurationMs { get; init; }

    /// <summary>
    /// Total time from event creation to processing (milliseconds).
    /// </summary>
    public int? TotalLagMs { get; init; }

    /// <summary>
    /// Current status of the event.
    /// Possible values: CREATED, PUBLISHED, PROCESSED, FAILED
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Reason for failure, if applicable.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// When this metrics record was created.
    /// </summary>
    public DateTime RecordedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Interface for tracking event processing lag.
///
/// Implementations are responsible for:
/// - Recording event lifecycle timestamps
/// - Calculating lag metrics
/// - Persisting metrics for observability
/// - Supporting querying of lag data
/// </summary>
public interface IEventLagTracker
{
    /// <summary>
    /// Record that an event was created.
    /// </summary>
    Task RecordEventCreatedAsync(
        string eventId,
        string eventName,
        string aggregateId,
        DateTime createdAt,
        CancellationToken cancellation = default);

    /// <summary>
    /// Record that an event was published to the message broker.
    /// </summary>
    Task RecordEventPublishedAsync(
        string eventId,
        DateTime publishedAt,
        int dispatchDurationMs,
        CancellationToken cancellation = default);

    /// <summary>
    /// Record that an event was processed by projections.
    /// </summary>
    Task RecordEventProcessedAsync(
        string eventId,
        DateTime processedAt,
        int processingDurationMs,
        CancellationToken cancellation = default);

    /// <summary>
    /// Record that an event processing failed.
    /// </summary>
    Task RecordEventFailedAsync(
        string eventId,
        string reason,
        CancellationToken cancellation = default);

    /// <summary>
    /// Get lag metrics for a specific event.
    /// </summary>
    Task<EventLagMetrics?> GetLagMetricsAsync(
        string eventId,
        CancellationToken cancellation = default);

    /// <summary>
    /// Get lag statistics for all events of a specific type.
    /// </summary>
    Task<EventLagStatistics?> GetStatisticsAsync(
        string eventName,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellation = default);

    /// <summary>
    /// Get top slowest events for debugging.
    /// </summary>
    Task<IEnumerable<EventLagMetrics>> GetSlowestEventsAsync(
        string? eventName = null,
        int limit = 10,
        CancellationToken cancellation = default);
}

/// <summary>
/// Aggregated lag statistics for event processing.
/// </summary>
public sealed record EventLagStatistics
{
    /// <summary>
    /// Event type being analyzed.
    /// </summary>
    public required string EventName { get; init; }

    /// <summary>
    /// Total number of events processed in the time window.
    /// </summary>
    public required int TotalEventsProcessed { get; init; }

    /// <summary>
    /// Number of failed events.
    /// </summary>
    public required int FailedEventsCount { get; init; }

    /// <summary>
    /// Average lag in milliseconds.
    /// </summary>
    public required decimal AverageLagMs { get; init; }

    /// <summary>
    /// Maximum lag observed in milliseconds.
    /// </summary>
    public required int MaxLagMs { get; init; }

    /// <summary>
    /// Minimum lag observed in milliseconds.
    /// </summary>
    public required int MinLagMs { get; init; }

    /// <summary>
    /// 50th percentile (median) lag.
    /// </summary>
    public required int P50LagMs { get; init; }

    /// <summary>
    /// 95th percentile lag.
    /// </summary>
    public required int P95LagMs { get; init; }

    /// <summary>
    /// 99th percentile lag.
    /// </summary>
    public required int P99LagMs { get; init; }

    /// <summary>
    /// Throughput in events per second.
    /// </summary>
    public required decimal EventsPerSecond { get; init; }

    /// <summary>
    /// Time period analyzed.
    /// </summary>
    public required (DateTime From, DateTime To) Period { get; init; }
}

/// <summary>
/// Exception thrown when lag tracking operations fail.
/// </summary>
public sealed class EventLagTrackingException : Exception
{
    public EventLagTrackingException(string message) : base(message) { }
    public EventLagTrackingException(string message, Exception inner) : base(message, inner) { }
}
