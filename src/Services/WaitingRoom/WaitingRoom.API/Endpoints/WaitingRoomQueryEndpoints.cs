namespace WaitingRoom.API.Endpoints;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Views;

/// <summary>
/// Query API endpoints for WaitingRoom monitoring projections.
///
/// Endpoints:
/// GET /api/v1/waiting-room/{queueId}/monitor → WaitingRoomMonitorView
/// GET /api/v1/waiting-room/{queueId}/queue-state → QueueStateView
/// POST /api/v1/waiting-room/{queueId}/rebuild → 202 Accepted
///
/// These endpoints return DENORMALIZED READ MODELS.
/// They are separate from the write model (commands).
/// This is CQRS separation: fast queries via projections.
///
/// All endpoints:
/// - Accept GET/POST with valid HTTP semantics
/// - Return JSON via minimal API
/// - Handle errors gracefully
/// - Include health/status checks
/// </summary>
public static class WaitingRoomQueryEndpoints
{
    /// <summary>
    /// Registers all query endpoints.
    /// Call from Program.cs:
    ///
    /// var group = app.MapGroup("/api/v1/waiting-room")
    ///     .WithTags("Waiting Room Queries");
    /// WaitingRoomQueryEndpoints.MapEndpoints(group);
    /// </summary>
    public static void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{queueId}/monitor", GetMonitorAsync)
            .WithName("GetMonitor")
            .WithSummary("Get waiting room monitor (KPI dashboard)")
            .WithDescription("Returns high-level metrics: patient counts by priority, average wait time, utilization.")
            .Produces<WaitingRoomMonitorView>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{queueId}/queue-state", GetQueueStateAsync)
            .WithName("GetQueueState")
            .WithSummary("Get queue current state (detailed view)")
            .WithDescription("Returns detailed queue state: patient list, capacity info, wait times.")
            .Produces<QueueStateView>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{queueId}/next-turn", GetNextTurnAsync)
            .WithName("GetNextTurn")
            .WithSummary("Get next operational turn")
            .WithDescription("Returns the currently claimed/called patient; if none, returns next waiting patient candidate.")
            .Produces<NextTurnView>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{queueId}/recent-history", GetRecentHistoryAsync)
            .WithName("GetRecentHistory")
            .WithSummary("Get recent completed attentions")
            .WithDescription("Returns recent completed attentions for operational traceability.")
            .Produces<IReadOnlyList<RecentAttentionRecordView>>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{queueId}/rebuild", RebuildProjectionAsync)
            .WithName("RebuildProjection")
            .WithSummary("Rebuild projection from events (async)")
            .WithDescription("Initiates full projection rebuild from event store. Returns 202 Accepted. Use for recovery or schema migration.")
            .Produces(StatusCodes.Status202Accepted)
            .Produces<ErrorResponse>(StatusCodes.Status500InternalServerError);
    }

    /// <summary>
    /// GET /api/v1/waiting-room/{queueId}/monitor
    ///
    /// Returns high-level KPI metrics for the queue.
    /// Used for operations dashboards and monitoring.
    /// </summary>
    private static async Task<IResult> GetMonitorAsync(
        string queueId,
        IWaitingRoomProjectionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            return Results.BadRequest(new ErrorResponse
            {
                Error = "QueueId required",
                StatusCode = 400
            });

        try
        {
            var view = await context.GetMonitorViewAsync(queueId, cancellationToken);

            if (view == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = $"Queue monitor not found for {queueId}",
                    StatusCode = 404
                });

            return Results.Ok(view);
        }
        catch (Exception)
        {
            return Results.InternalServerError();
        }
    }

    /// <summary>
    /// GET /api/v1/waiting-room/{queueId}/queue-state
    ///
    /// Returns detailed queue state with patient list.
    /// Used for detailed monitoring and patient management.
    /// </summary>
    private static async Task<IResult> GetQueueStateAsync(
        string queueId,
        IWaitingRoomProjectionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            return Results.BadRequest(new ErrorResponse
            {
                Error = "QueueId required",
                StatusCode = 400
            });

        try
        {
            var view = await context.GetQueueStateViewAsync(queueId, cancellationToken);

            if (view == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = $"Queue state not found for {queueId}",
                    StatusCode = 404
                });

            return Results.Ok(view);
        }
        catch (Exception)
        {
            return Results.InternalServerError();
        }
    }

    private static async Task<IResult> GetNextTurnAsync(
        string queueId,
        IWaitingRoomProjectionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            return Results.BadRequest(new ErrorResponse
            {
                Error = "QueueId required",
                StatusCode = 400
            });

        try
        {
            var activeTurn = await context.GetNextTurnViewAsync(queueId, cancellationToken);
            if (activeTurn != null)
                return Results.Ok(activeTurn);

            var queue = await context.GetQueueStateViewAsync(queueId, cancellationToken);
            if (queue == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = $"Queue state not found for {queueId}",
                    StatusCode = 404
                });

            var candidate = queue.PatientsInQueue.FirstOrDefault();
            if (candidate == null)
                return Results.NotFound(new ErrorResponse
                {
                    Error = $"No turn available for {queueId}",
                    StatusCode = 404
                });

            return Results.Ok(new NextTurnView
            {
                QueueId = queueId,
                PatientId = candidate.PatientId,
                PatientName = candidate.PatientName,
                Priority = candidate.Priority,
                ConsultationType = "Pending",
                Status = "waiting",
                ClaimedAt = null,
                CalledAt = null,
                StationId = null,
                ProjectedAt = DateTimeOffset.UtcNow
            });
        }
        catch (Exception)
        {
            return Results.InternalServerError();
        }
    }

    private static async Task<IResult> GetRecentHistoryAsync(
        string queueId,
        int? limit,
        IWaitingRoomProjectionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            return Results.BadRequest(new ErrorResponse
            {
                Error = "QueueId required",
                StatusCode = 400
            });

        try
        {
            var effectiveLimit = limit.GetValueOrDefault(20);
            if (effectiveLimit <= 0)
                effectiveLimit = 20;

            var history = await context.GetRecentAttentionHistoryAsync(queueId, effectiveLimit, cancellationToken);
            return Results.Ok(history);
        }
        catch (Exception)
        {
            return Results.InternalServerError();
        }
    }

    /// <summary>
    /// POST /api/v1/waiting-room/{queueId}/rebuild
    ///
    /// Initiates asynchronous projection rebuild.
    /// Returns 202 Accepted immediately.
    /// Rebuild happens in background.
    ///
    /// Use cases:
    /// - Full recovery from projection failure
    /// - Schema migration
    /// - Verification (rebuild and compare)
    /// </summary>
    private static async Task<IResult> RebuildProjectionAsync(
        string queueId,
        IProjection projection,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queueId))
            return Results.BadRequest(new ErrorResponse
            {
                Error = "QueueId required",
                StatusCode = 400
            });

        try
        {
            // Fire off async rebuild (don't await)
            // In real app, would use background service/queue
            _ = Task.Run(
                async () => await projection.RebuildAsync(cancellationToken),
                cancellationToken);

            return Results.Accepted(
                $"/api/v1/waiting-room/{queueId}/monitor",
                new { message = "Projection rebuild initiated", queueId });
        }
        catch (Exception)
        {
            return Results.InternalServerError();
        }
    }
}

/// <summary>
/// Standard error response format.
/// Used consistently across API.
/// </summary>
public record ErrorResponse
{
    public required string Error { get; init; }
    public required int StatusCode { get; init; }
    public string? Detail { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
