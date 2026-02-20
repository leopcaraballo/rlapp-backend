namespace WaitingRoom.Application.CommandHandlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Domain.Commands;
using WaitingRoom.Domain.ValueObjects;

/// <summary>
/// Handles the CheckInPatientCommand.
///
/// Responsibility:
/// - Orchest rate the check-in use case
/// - Load the waiting queue aggregate
/// - Execute domain logic via the aggregate
/// - Persist domain events
/// - Publish events for downstream consumers
///
/// This handler is PURE ORCHESTRATION.
/// All business rules are in the Domain (WaitingQueue aggregate).
///
/// Pattern: CQRS Command Handler + Event Sourcing
/// </summary>
public sealed class CheckInPatientCommandHandler
{
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;

    public CheckInPatientCommandHandler(
        IEventStore eventStore,
        IEventPublisher eventPublisher,
        IClock clock)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Executes the CheckInPatient command.
    ///
    /// Steps:
    /// 1. Load aggregate from event store (reconstruct from event history)
    /// 2. Execute domain operation on aggregate (CheckInPatient)
    /// 3. Persist new events atomically
    /// 4. Publish events to event bus (for projections and external services)
    /// 5. Return success
    ///
    /// If anything fails, aggregate remains unchanged in event store.
    /// </summary>
    /// <param name="command">The check-in command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Event count indicating success.</returns>
    /// <exception cref="AggregateNotFoundException">If queue not found.</exception>
    /// <exception cref="EventConflictException">If version conflict (concurrent modification).</exception>
    /// <exception cref="DomainException">If domain invariant violation (from Domain layer).</exception>
    public async Task<int> HandleAsync(
        CheckInPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        // STEP 1: Load the aggregate from event store
        // This reconstructs the complete aggregate state from all past events
        var queue = await _eventStore.LoadAsync(command.QueueId, cancellationToken)
            ?? throw new AggregateNotFoundException(command.QueueId);

        // STEP 2: Execute domain logic
        // The aggregate applies all business rules to verify check-in is valid
        // If invalid, it throws DomainException
        // If valid, it generates PatientCheckedIn event and applies it to state

        // Build metadata once
        var metadata = EventMetadata.CreateNew(
            aggregateId: command.QueueId,
            actor: command.Actor,
            correlationId: command.CorrelationId ?? Guid.NewGuid().ToString());

        // Create request object (Parameter Object pattern, eliminates 7-parameter method)
        var request = new CheckInPatientRequest
        {
            PatientId = PatientId.Create(command.PatientId),
            PatientName = command.PatientName,
            Priority = Priority.Create(command.Priority),
            ConsultationType = ConsultationType.Create(command.ConsultationType),
            CheckInTime = _clock.UtcNow,
            Metadata = metadata,
            Notes = command.Notes
        };

        // Execute domain operation with single parameter
        queue.CheckInPatient(request);

        // STEP 3: Persist events atomically
        // All new events (in UncommittedEvents) are saved in single transaction
        // Version conflict is detected here if another handler modified same aggregate
        var eventsToPublish = queue.UncommittedEvents.ToList();
        await _eventStore.SaveAsync(queue, cancellationToken);

        // STEP 4: Publish events
        // After successful persistence, publish all new events
        // This triggers projections and external subscribers
        if (eventsToPublish.Count > 0)
        {
            await _eventPublisher.PublishAsync(eventsToPublish, cancellationToken);
        }

        // STEP 5: Return result
        return eventsToPublish.Count;
    }
}
