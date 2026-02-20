namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;
using WaitingRoom.Projections.Views;

public sealed class PatientPaymentValidatedProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(PatientPaymentValidated);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not PatientPaymentValidated evt)
            throw new ArgumentException($"Expected {nameof(PatientPaymentValidated)}, got {@event.GetType().Name}");

        if (context is not IWaitingRoomProjectionContext waitingContext)
            throw new InvalidOperationException($"Context must implement {nameof(IWaitingRoomProjectionContext)}");

        var idempotencyKey = $"patient-payment-validated:{evt.QueueId}:{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        var normalizedPriority = NormalizePriority(evt.Priority);

        await waitingContext.UpdateMonitorViewAsync(evt.QueueId, normalizedPriority, "increment", cancellationToken);
        await waitingContext.AddPatientToQueueAsync(
            evt.QueueId,
            new PatientInQueueDto
            {
                PatientId = evt.PatientId,
                PatientName = evt.PatientName,
                Priority = normalizedPriority,
                CheckInTime = evt.ValidatedAt,
                WaitTimeMinutes = 0
            },
            cancellationToken);

        await waitingContext.SetNextTurnViewAsync(evt.QueueId, null, cancellationToken);
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
