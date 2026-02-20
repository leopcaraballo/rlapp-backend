namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;

public sealed class ConsultingRoomDeactivatedProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(ConsultingRoomDeactivated);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not ConsultingRoomDeactivated evt)
            throw new ArgumentException($"Expected {nameof(ConsultingRoomDeactivated)}, got {@event.GetType().Name}");

        var idempotencyKey = $"consulting-room-deactivated:{evt.QueueId}:{evt.ConsultingRoomId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        await context.MarkProcessedAsync(idempotencyKey, cancellationToken);
    }
}
