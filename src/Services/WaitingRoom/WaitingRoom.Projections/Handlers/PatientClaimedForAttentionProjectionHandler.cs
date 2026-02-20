namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Views;

public sealed class PatientClaimedForAttentionProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(PatientClaimedForAttention);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not PatientClaimedForAttention evt)
            throw new ArgumentException($"Expected {nameof(PatientClaimedForAttention)}, got {@event.GetType().Name}");

        if (context is not IWaitingRoomProjectionContext waitingContext)
            throw new InvalidOperationException($"Context must implement {nameof(IWaitingRoomProjectionContext)}");

        var idempotencyKey = $"patient-claimed:{evt.QueueId}:{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        var normalizedPriority = NormalizePriority(evt.Priority);

        await waitingContext.UpdateMonitorViewAsync(evt.QueueId, normalizedPriority, "decrement", cancellationToken);
        await waitingContext.RemovePatientFromQueueAsync(evt.QueueId, evt.PatientId, cancellationToken);

        await waitingContext.SetNextTurnViewAsync(
            evt.QueueId,
            new NextTurnView
            {
                QueueId = evt.QueueId,
                PatientId = evt.PatientId,
                PatientName = evt.PatientName,
                Priority = normalizedPriority,
                ConsultationType = evt.ConsultationType,
                Status = "claimed",
                ClaimedAt = evt.ClaimedAt,
                StationId = evt.StationId,
                ProjectedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        await context.MarkProcessedAsync(idempotencyKey, cancellationToken);
    }

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
