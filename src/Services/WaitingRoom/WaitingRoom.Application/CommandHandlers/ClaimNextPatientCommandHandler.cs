namespace WaitingRoom.Application.CommandHandlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Commands;

public sealed class ClaimNextPatientCommandHandler
{
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;

    public ClaimNextPatientCommandHandler(
        IEventStore eventStore,
        IEventPublisher eventPublisher,
        IClock clock)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<(int EventCount, string PatientId)> HandleAsync(
        ClaimNextPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        var queue = await _eventStore.LoadAsync(command.QueueId, cancellationToken)
            ?? throw new AggregateNotFoundException(command.QueueId);

        var metadata = EventMetadata.CreateNew(
            aggregateId: command.QueueId,
            actor: command.Actor,
            correlationId: command.CorrelationId ?? Guid.NewGuid().ToString());

        var request = new ClaimNextPatientRequest
        {
            ClaimedAt = _clock.UtcNow,
            Metadata = metadata,
            StationId = command.StationId
        };

        var patientId = queue.ClaimNextPatient(request);

        var eventsToPublish = queue.UncommittedEvents.ToList();
        await _eventStore.SaveAsync(queue, cancellationToken);

        if (eventsToPublish.Count > 0)
            await _eventPublisher.PublishAsync(eventsToPublish, cancellationToken);

        return (eventsToPublish.Count, patientId);
    }
}
