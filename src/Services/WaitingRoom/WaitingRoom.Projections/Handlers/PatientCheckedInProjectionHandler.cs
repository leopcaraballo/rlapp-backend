namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Views;

/// <summary>
/// Projection handler for PatientCheckedIn domain event.
///
/// This handler:
/// - Listens for PatientCheckedIn events from WaitingQueue aggregate
/// - Updates WaitingRoomMonitorView (counts by priority)
/// - Updates QueueStateView (patient list)
/// - Maintains idempotency
/// - Supports deterministic replay
///
/// Key responsibility:
/// - Transform domain event into denormalized read model data
/// - Ensure idempotency via idempotency key
/// - Track processing via checkpoint
///
/// NO business logic here. Only data transformation.
/// </summary>
public sealed class PatientCheckedInProjectionHandler : IProjectionHandler
{
    /// <summary>
    /// Domain event type this handler processes.
    /// </summary>
    public string EventName => nameof(PatientCheckedIn);

    /// <summary>
    /// Handles PatientCheckedIn event idempotently.
    ///
    /// Process:
    /// 1. Check if already processed (idempotency)
    /// 2. Extract event data
    /// 3. Update monitor view (increment counters)
    /// 4. Update queue state view (add patient)
    /// 5. Mark as processed
    ///
    /// Invariant: Same event processed twice = identical state.
    /// </summary>
    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not PatientCheckedIn evt)
            throw new ArgumentException($"Expected {nameof(PatientCheckedIn)}, got {@event.GetType().Name}");

        // Cast to extended context (should always work in practice)
        if (context is not IWaitingRoomProjectionContext waitingContext)
            throw new InvalidOperationException($"Context must implement {nameof(IWaitingRoomProjectionContext)}");

        // Idempotency key prevents duplicate processing
        var idempotencyKey = GenerateIdempotencyKey(evt);

        // Check if already processed
        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return; // Already processed, skip

        // ✅ Update WaitingRoomMonitorView
        // Increment counters based on priority
        await waitingContext.UpdateMonitorViewAsync(
            queueId: evt.QueueId,
            priority: NormalizePriority(evt.Priority),
            operation: "increment",
            cancellationToken);

        // ✅ Update QueueStateView
        // Add patient to queue list
        await waitingContext.AddPatientToQueueAsync(
            queueId: evt.QueueId,
            patient: new PatientInQueueDto
            {
                PatientId = evt.PatientId,
                PatientName = evt.PatientName,
                Priority = NormalizePriority(evt.Priority),
                CheckInTime = evt.Metadata.OccurredAt,
                WaitTimeMinutes = 0
            },
            cancellationToken);

        // ✅ Mark as processed to prevent duplicate
        await context.MarkProcessedAsync(idempotencyKey, cancellationToken);
    }

    /// <summary>
    /// Generates deterministic idempotency key for this event.
    /// Same event always produces same key.
    /// Key format: "queueId:aggregateId:eventId"
    /// </summary>
    private static string GenerateIdempotencyKey(PatientCheckedIn evt)
        => $"patient-checked-in:{evt.QueueId}:{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";

    private static string NormalizePriority(string priority)
    {
        var normalized = priority.Trim().ToLowerInvariant();

        return normalized switch
        {
            "urgent" => "high",
            "high" => "high",
            "medium" => "normal",
            "normal" => "normal",
            "low" => "low",
            _ => normalized
        };
    }
}
