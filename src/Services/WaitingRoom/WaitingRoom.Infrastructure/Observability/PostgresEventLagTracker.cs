namespace WaitingRoom.Infrastructure.Observability;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

/// <summary>
/// PostgreSQL implementation of IEventLagTracker.
///
/// Persists event lag metrics to PostgreSQL for:
/// - Long-term analysis
/// - Alerting
/// - Visualization in Grafana
/// - Debugging event processing bottlenecks
/// </summary>
internal sealed class PostgresEventLagTracker : IEventLagTracker
{
    private readonly string _connectionString;

    public PostgresEventLagTracker(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string required", nameof(connectionString));

        _connectionString = connectionString;
    }

    public async Task RecordEventCreatedAsync(
        string eventId,
        string eventName,
        string aggregateId,
        DateTime createdAt,
        CancellationToken cancellation = default)
    {
        const string sql = @"
INSERT INTO event_processing_lag (event_name, aggregate_id, event_created_at, status, created_at)
VALUES (@EventName, @AggregateId, @EventCreatedAt, 'CREATED', @CreatedAt)
ON CONFLICT (event_id) DO UPDATE SET
    status = 'CREATED',
    event_created_at = @EventCreatedAt;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { EventName = eventName, AggregateId = aggregateId, EventCreatedAt = createdAt, CreatedAt = DateTime.UtcNow },
                    cancellationToken: cancellation));
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException($"Failed to record event created: {eventId}", ex);
        }
    }

    public async Task RecordEventPublishedAsync(
        string eventId,
        DateTime publishedAt,
        int dispatchDurationMs,
        CancellationToken cancellation = default)
    {
        const string sql = @"
UPDATE event_processing_lag
SET
    event_published_at = @PublishedAt,
    outbox_dispatch_duration_ms = @DispatchDurationMs,
    status = 'PUBLISHED'
WHERE event_id = @EventId;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { EventId = eventId, PublishedAt = publishedAt, DispatchDurationMs = dispatchDurationMs },
                    cancellationToken: cancellation));
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException($"Failed to record event published: {eventId}", ex);
        }
    }

    public async Task RecordEventProcessedAsync(
        string eventId,
        DateTime processedAt,
        int processingDurationMs,
        CancellationToken cancellation = default)
    {
        const string sql = @"
UPDATE event_processing_lag
SET
    projection_processed_at = @ProcessedAt,
    projection_processing_duration_ms = @ProcessingDurationMs,
    total_lag_ms = EXTRACT(EPOCH FROM (@ProcessedAt - event_created_at))::INT * 1000,
    status = 'PROCESSED'
WHERE event_id = @EventId;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { EventId = eventId, ProcessedAt = processedAt, ProcessingDurationMs = processingDurationMs },
                    cancellationToken: cancellation));
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException($"Failed to record event processed: {eventId}", ex);
        }
    }

    public async Task RecordEventFailedAsync(
        string eventId,
        string reason,
        CancellationToken cancellation = default)
    {
        const string sql = @"
UPDATE event_processing_lag
SET status = 'FAILED'
WHERE event_id = @EventId;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    sql,
                    new { EventId = eventId },
                    cancellationToken: cancellation));
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException($"Failed to record event failed: {eventId}", ex);
        }
    }

    public async Task<EventLagMetrics?> GetLagMetricsAsync(
        string eventId,
        CancellationToken cancellation = default)
    {
        const string sql = @"
SELECT
    event_id as EventId,
    event_name as EventName,
    aggregate_id as AggregateId,
    event_created_at as EventCreatedAt,
    event_published_at as EventPublishedAt,
    outbox_dispatch_duration_ms as OutboxDispatchDurationMs,
    projection_processed_at as EventProcessedAt,
    projection_processing_duration_ms as ProjectionProcessingDurationMs,
    total_lag_ms as TotalLagMs,
    status as Status
FROM event_processing_lag
WHERE event_id = @EventId;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            var result = await connection.QueryFirstOrDefaultAsync<EventLagMetrics>(
                new CommandDefinition(
                    sql,
                    new { EventId = eventId },
                    cancellationToken: cancellation));
            return result;
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException($"Failed to get lag metrics for event: {eventId}", ex);
        }
    }

    public async Task<EventLagStatistics?> GetStatisticsAsync(
        string eventName,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellation = default)
    {
        const string sql = @"
SELECT
    COUNT(*) as TotalEventsProcessed,
    COUNT(CASE WHEN status = 'FAILED' THEN 1 END) as FailedEventsCount,
    ROUND(AVG(total_lag_ms)::NUMERIC, 2) as AverageLagMs,
    MAX(total_lag_ms) as MaxLagMs,
    MIN(COALESCE(total_lag_ms, 0)) as MinLagMs,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY total_lag_ms) as P50LagMs,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY total_lag_ms) as P95LagMs,
    PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY total_lag_ms) as P99LagMs,
    (COUNT(*) / NULLIF(EXTRACT(EPOCH FROM MAX(created_at) - MIN(created_at)), 0))::NUMERIC as EventsPerSecond
FROM event_processing_lag
WHERE event_name = @EventName
  AND (@From IS NULL OR created_at >= @From)
  AND (@To IS NULL OR created_at <= @To)
  AND status = 'PROCESSED';";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            var result = await connection.QueryFirstOrDefaultAsync(
                new CommandDefinition(
                    sql,
                    new { EventName = eventName, From = from, To = to },
                    cancellationToken: cancellation));

            if (result == null) return null;

            return new EventLagStatistics
            {
                EventName = eventName,
                TotalEventsProcessed = result.TotalEventsProcessed,
                FailedEventsCount = result.FailedEventsCount,
                AverageLagMs = result.AverageLagMs,
                MaxLagMs = result.MaxLagMs,
                MinLagMs = result.MinLagMs ?? 0,
                P50LagMs = (int?)(result.P50LagMs ?? 0) ?? 0,
                P95LagMs = (int?)(result.P95LagMs ?? 0) ?? 0,
                P99LagMs = (int?)(result.P99LagMs ?? 0) ?? 0,
                EventsPerSecond = result.EventsPerSecond ?? 0m,
                Period = (from ?? DateTime.UtcNow.AddHours(-1), to ?? DateTime.UtcNow)
            };
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException($"Failed to get statistics for event: {eventName}", ex);
        }
    }

    public async Task<IEnumerable<EventLagMetrics>> GetSlowestEventsAsync(
        string? eventName = null,
        int limit = 10,
        CancellationToken cancellation = default)
    {
        var sql = @"
SELECT
    event_id as EventId,
    event_name as EventName,
    aggregate_id as AggregateId,
    event_created_at as EventCreatedAt,
    event_published_at as EventPublishedAt,
    outbox_dispatch_duration_ms as OutboxDispatchDurationMs,
    projection_processed_at as EventProcessedAt,
    projection_processing_duration_ms as ProjectionProcessingDurationMs,
    total_lag_ms as TotalLagMs,
    status as Status
FROM event_processing_lag
WHERE (@EventName IS NULL OR event_name = @EventName)
  AND status = 'PROCESSED'
ORDER BY total_lag_ms DESC NULLS LAST
LIMIT @Limit;";

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            var results = await connection.QueryAsync<EventLagMetrics>(
                new CommandDefinition(
                    sql,
                    new { EventName = eventName, Limit = limit },
                    cancellationToken: cancellation));
            return results.ToList();
        }
        catch (Exception ex)
        {
            throw new EventLagTrackingException("Failed to get slowest events", ex);
        }
    }
}
