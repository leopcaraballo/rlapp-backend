namespace WaitingRoom.Application.CommandHandlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.ValueObjects;

public sealed class ValidatePaymentCommandHandler
{
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;

    public ValidatePaymentCommandHandler(
        IEventStore eventStore,
        IEventPublisher eventPublisher,
        IClock clock)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<int> HandleAsync(
        ValidatePaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        var queue = await _eventStore.LoadAsync(command.QueueId, cancellationToken)
            ?? throw new AggregateNotFoundException(command.QueueId);

        var metadata = EventMetadata.CreateNew(
            aggregateId: command.QueueId,
            actor: command.Actor,
            correlationId: command.CorrelationId ?? Guid.NewGuid().ToString());

        var request = new ValidatePaymentRequest
        {
            PatientId = PatientId.Create(command.PatientId),
            ValidatedAt = _clock.UtcNow,
            Metadata = metadata,
            PaymentReference = command.PaymentReference
        };

        queue.ValidatePayment(request);

        var eventsToPublish = queue.UncommittedEvents.ToList();
        await _eventStore.SaveAsync(queue, cancellationToken);

        if (eventsToPublish.Count > 0)
            await _eventPublisher.PublishAsync(eventsToPublish, cancellationToken);

        return eventsToPublish.Count;
    }
}
