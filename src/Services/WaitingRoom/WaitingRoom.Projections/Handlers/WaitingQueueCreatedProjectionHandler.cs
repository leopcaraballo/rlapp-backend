namespace WaitingRoom.Projections.Handlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Projections.Abstractions;

public sealed class WaitingQueueCreatedProjectionHandler : IProjectionHandler
{
    public string EventName => nameof(WaitingQueueCreated);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellationToken = default)
    {
        if (@event is not WaitingQueueCreated evt)
            throw new ArgumentException($"Expected {nameof(WaitingQueueCreated)}, got {@event.GetType().Name}");

        var idempotencyKey = $"queue-created:{evt.QueueId}:{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";

        if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
            return;

        await context.MarkProcessedAsync(idempotencyKey, cancellationToken);
    }
}
