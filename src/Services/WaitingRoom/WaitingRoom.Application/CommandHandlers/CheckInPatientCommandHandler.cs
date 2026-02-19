namespace WaitingRoom.Application.CommandHandlers;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Application.Commands;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Aggregates;
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

    public CheckInPatientCommandHandler(
        IEventStore eventStore,
        IEventPublisher eventPublisher)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
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
        var metadata = EventMetadata.CreateNew(
            aggregateId: command.QueueId,
            actor: command.Actor,
            correlationId: command.CorrelationId ?? Guid.NewGuid().ToString());

        var patientId = PatientId.Create(command.PatientId);
        var priority = Priority.Create(command.Priority);
        var consultationType = ConsultationType.Create(command.ConsultationType);

        queue.CheckInPatient(
            patientId: patientId,
            patientName: command.PatientName,
            priority: priority,
            consultationType: consultationType,
            checkInTime: DateTime.UtcNow,
            metadata: metadata,
            notes: command.Notes);

        // STEP 3: Persist events atomically
        // All new events (in UncommittedEvents) are saved in single transaction
        // Version conflict is detected here if another handler modified same aggregate
        await _eventStore.SaveAsync(queue, cancellationToken);

        // STEP 4: Publish events
        // After successful persistence, publish all new events
        // This triggers projections and external subscribers
        if (queue.HasUncommittedEvents)
        {
            await _eventPublisher.PublishAsync(queue.UncommittedEvents, cancellationToken);
        }

        // STEP 5: Return result
        var eventCount = queue.UncommittedEvents.Count;
        return eventCount;
    }
}
