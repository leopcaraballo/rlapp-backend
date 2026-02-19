namespace WaitingRoom.Projections.Processing;

using BuildingBlocks.EventSourcing;
using Microsoft.Extensions.Logging;
using WaitingRoom.Infrastructure.Observability;
using WaitingRoom.Projections.Abstractions;

/// <summary>
/// Processes domain events through projections.
///
/// Responsibilities:
/// - Route events to appropriate handlers
/// - Update projection state
/// - Track processing lag
/// - Handle failures gracefully
/// - Maintain idempotency
///
/// Architecture:
/// - Receives events from subscriber
/// - Finds matching handlers
/// - Executes handlers
/// - Records metrics
/// - Updates checkpoints
/// </summary>
public sealed class ProjectionEventProcessor
{
    private readonly IProjection _projection;
    private readonly IEventLagTracker _lagTracker;
    private readonly ILogger<ProjectionEventProcessor> _logger;

    public ProjectionEventProcessor(
        IProjection projection,
        IEventLagTracker lagTracker,
        ILogger<ProjectionEventProcessor> logger)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _lagTracker = lagTracker ?? throw new ArgumentNullException(nameof(lagTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Process a single event through the projection.
    ///
    /// Process:
    /// 1. Find matching handler for event type
    /// 2. Execute handler
    /// 3. Record lag metrics
    /// 4. Update checkpoint
    /// 5. Log success or failure
    /// </summary>
    public async Task ProcessEventAsync(
        DomainEvent @event,
        CancellationToken cancellation = default)
    {
        var startTime = DateTime.UtcNow;
        var eventType = @event.GetType().Name;

        try
        {
            _logger.LogDebug(
                "Processing event {EventType} (aggregate: {AggregateId}) for projection {ProjectionId}",
                eventType,
                @event.AggregateId,
                _projection.ProjectionId);

            // Delegate processing to projection engine
            await _projection.ProcessEventAsync(@event, cancellation);

            var processingDurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            // Record lag metrics
            await _lagTracker.RecordEventProcessedAsync(
                eventId: @event.EventId,
                processedAt: DateTime.UtcNow,
                processingDurationMs: processingDurationMs,
                cancellation: cancellation);

            _logger.LogInformation(
                "Successfully processed event {EventType} for projection {ProjectionId} (duration: {Duration}ms)",
                eventType,
                _projection.ProjectionId,
                processingDurationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error processing event {EventType} in projection {ProjectionId}",
                eventType,
                _projection.ProjectionId);

            // Record failure for monitoring
            await _lagTracker.RecordEventFailedAsync(
                eventId: @event.EventId,
                reason: ex.Message,
                cancellation: cancellation);

            throw;
        }
    }

    /// <summary>
    /// Rebuild projection from scratch.
    ///
    /// Used for:
    /// - Initial population
    /// - Schema changes
    /// - Disaster recovery
    /// - Data verification
    /// </summary>
    public async Task RebuildAsync(CancellationToken cancellation = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation(
                "Starting projection rebuild for {ProjectionId}",
                _projection.ProjectionId);

            await _projection.RebuildAsync(cancellation);

            var durationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

            _logger.LogInformation(
                "Projection rebuild completed for {ProjectionId} (duration: {Duration}ms)",
                _projection.ProjectionId,
                durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Projection rebuild failed for {ProjectionId}",
                _projection.ProjectionId);
            throw;
        }
    }

    /// <summary>
    /// Get current projection health status.
    /// </summary>
    public async Task<ProjectionHealth> GetHealthAsync(CancellationToken cancellation = default)
    {
        try
        {
            var checkpoint = await _projection.GetCheckpointAsync(cancellation);

            // Calculate lag if checkpoint exists
            long? lagMs = null;
            if (checkpoint != null)
            {
                lagMs = (long)(DateTime.UtcNow - checkpoint.LastProcessedAt).TotalMilliseconds;
            }

            return new ProjectionHealth
            {
                IsHealthy = checkpoint != null && lagMs < 30000, // Healthy if <30s lag
                LastUpdatedAt = checkpoint?.LastProcessedAt ?? DateTime.UtcNow,
                LagMs = lagMs,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            return new ProjectionHealth
            {
                IsHealthy = false,
                LastUpdatedAt = DateTime.UtcNow,
                ErrorMessage = ex.Message
            };
        }
    }

}
