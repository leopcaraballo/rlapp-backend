namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;

public sealed class ConsultingRoomActivatedProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(ConsultingRoomActivated);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not ConsultingRoomActivated evt)
            throw new ArgumentException($"Expected {nameof(ConsultingRoomActivated)}, got {@event.GetType().Name}");

        var idempotencyKey = $"consulting-room-activated:{evt.QueueId}:{evt.ConsultingRoomId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        await context.MarkProcessedAsync(idempotencyKey, cancellationToken);
    }
}
