namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;

public sealed class PatientCalledProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(PatientCalled);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not PatientCalled evt)
            throw new ArgumentException($"Expected {nameof(PatientCalled)}, got {@event.GetType().Name}");

        if (context is not IWaitingRoomProjectionContext waitingContext)
            throw new InvalidOperationException($"Context must implement {nameof(IWaitingRoomProjectionContext)}");

        var idempotencyKey = $"patient-called:{evt.QueueId}:{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        var currentTurn = await waitingContext.GetNextTurnViewAsync(evt.QueueId, cancellationToken);
        if (currentTurn != null && string.Equals(currentTurn.PatientId, evt.PatientId, StringComparison.OrdinalIgnoreCase))
        {
            await waitingContext.SetNextTurnViewAsync(
                evt.QueueId,
                currentTurn with
                {
                    Status = "called",
                    CalledAt = evt.CalledAt,
                    ProjectedAt = DateTimeOffset.UtcNow
                },
                cancellationToken);
        }

        await context.MarkProcessedAsync(idempotencyKey, cancellationToken);
    }
}
