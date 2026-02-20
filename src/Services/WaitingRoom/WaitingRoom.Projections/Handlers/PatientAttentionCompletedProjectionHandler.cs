namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Views;

public sealed class PatientAttentionCompletedProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(PatientAttentionCompleted);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not PatientAttentionCompleted evt)
            throw new ArgumentException($"Expected {nameof(PatientAttentionCompleted)}, got {@event.GetType().Name}");

        if (context is not IWaitingRoomProjectionContext waitingContext)
            throw new InvalidOperationException($"Context must implement {nameof(IWaitingRoomProjectionContext)}");

        var idempotencyKey = $"patient-attention-completed:{evt.QueueId}:{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        await waitingContext.SetNextTurnViewAsync(evt.QueueId, null, cancellationToken);

        await waitingContext.AddRecentAttentionRecordAsync(
            evt.QueueId,
            new RecentAttentionRecordView
            {
                QueueId = evt.QueueId,
                PatientId = evt.PatientId,
                PatientName = evt.PatientName,
                Priority = NormalizePriority(evt.Priority),
                ConsultationType = evt.ConsultationType,
                CompletedAt = evt.CompletedAt,
                Outcome = evt.Outcome,
                Notes = evt.Notes
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
