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
            .WithOpenApi()
            .WithSummary("Get waiting room monitor (KPI dashboard)")
            .WithDescription("Returns high-level metrics: patient counts by priority, average wait time, utilization.")
            .Produces<WaitingRoomMonitorView>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapGet("/{queueId}/queue-state", GetQueueStateAsync)
            .WithName("GetQueueState")
            .WithOpenApi()
            .WithSummary("Get queue current state (detailed view)")
            .WithDescription("Returns detailed queue state: patient list, capacity info, wait times.")
            .Produces<QueueStateView>(StatusCodes.Status200OK)
            .Produces<ErrorResponse>(StatusCodes.Status404NotFound);

        group.MapPost("/{queueId}/rebuild", RebuildProjectionAsync)
            .WithName("RebuildProjection")
            .WithOpenApi()
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
        catch (Exception ex)
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
        catch (Exception ex)
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
        catch (Exception ex)
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
