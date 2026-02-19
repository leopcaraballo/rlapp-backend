namespace WaitingRoom.Domain.Aggregates;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.Events;
using WaitingRoom.Domain.ValueObjects;
using WaitingRoom.Domain.Entities;
using WaitingRoom.Domain.Invariants;
using WaitingRoom.Domain.Exceptions;

/// <summary>
/// Aggregate Root for the WaitingRoom domain.
///
/// Responsibilities:
/// - Protect waiting room invariants
/// - Generate PatientCheckedIn events
/// - Reconstruct state from event history
///
/// Invariants enforced:
/// - Queue never exceeds capacity
/// - No duplicate patient check-ins
/// - Valid priorities only
/// - Queue maintains patient order
///
/// This is pure domain logic with NO infrastructure dependencies.
/// </summary>
public sealed class WaitingQueue : AggregateRoot
{
    /// <summary>
    /// Unique name/identifier for the queue.
    /// </summary>
    public string QueueName { get; private set; } = string.Empty;

    /// <summary>
    /// Maximum patients allowed in queue simultaneously.
    /// </summary>
    public int MaxCapacity { get; private set; }

    /// <summary>
    /// Current patients in the waiting queue (ordered).
    /// </summary>
    public List<WaitingPatient> Patients { get; private set; } = [];

    /// <summary>
    /// When queue was created.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// When queue was last updated.
    /// </summary>
    public DateTime LastModifiedAt { get; private set; }

    private WaitingQueue() { }

    /// <summary>
    /// Creates a new waiting queue with initial capacity and metadata.
    /// </summary>
    /// <param name="queueId">Unique queue identifier.</param>
    /// <param name="queueName">Human-readable queue name.</param>
    /// <param name="maxCapacity">Maximum patients allowed.</param>
    /// <param name="metadata">Event metadata for traceability.</param>
    /// <returns>New WaitingQueue aggregate.</returns>
    public static WaitingQueue Create(
        string queueId,
        string queueName,
        int maxCapacity,
        EventMetadata metadata)
    {
        WaitingQueueInvariants.ValidateQueueName(queueName);

        if (maxCapacity <= 0)
            throw new DomainException("MaxCapacity must be greater than 0");

        var queue = new WaitingQueue
        {
            Id = queueId,
            QueueName = queueName,
            MaxCapacity = maxCapacity,
            CreatedAt = metadata.OccurredAt,
            LastModifiedAt = metadata.OccurredAt,
            Version = 0
        };

        return queue;
    }

    /// <summary>
    /// Core use case: Patient checks into waiting room.
    ///
    /// Business logic:
    /// 1. Validate queue hasn't reached capacity
    /// 2. Validate patient isn't already checked in
    /// 3. Validate priority and consultation type
    /// 4. Assign position based on priority
    /// 5. Emit PatientCheckedIn event
    /// 6. Apply event to state (idempotent)
    /// </summary>
    public void CheckInPatient(
        PatientId patientId,
        string patientName,
        Priority priority,
        ConsultationType consultationType,
        DateTime checkInTime,
        EventMetadata metadata,
        string? notes = null)
    {
        // Validate invariants
        WaitingQueueInvariants.ValidateCapacity(Patients.Count, MaxCapacity);
        WaitingQueueInvariants.ValidateDuplicateCheckIn(patientId.Value, Patients.Select(p => p.PatientId.Value));
        WaitingQueueInvariants.ValidatePriority(priority.Value);

        // Calculate queue position (could be priority-based in future)
        int queuePosition = Patients.Count;

        // Create and raise event
        var @event = new PatientCheckedIn
        {
            Metadata = metadata.WithVersion(Version + 1),
            QueueId = Id,
            PatientId = patientId.Value,
            PatientName = patientName,
            Priority = priority.Value,
            ConsultationType = consultationType.Value,
            CheckInTime = checkInTime,
            QueuePosition = queuePosition,
            Notes = notes
        };

        RaiseEvent(@event);
    }

    /// <summary>
    /// Event handler: Apply PatientCheckedIn event to state.
    /// This method is invoked via reflection by the AggregateRoot base class.
    ///
    /// Must be idempotent: applying same event twice = same state.
    /// </summary>
    private void When(PatientCheckedIn @event)
    {
        var patient = new WaitingPatient(
            patientId: PatientId.Create(@event.PatientId),
            patientName: @event.PatientName,
            priority: Priority.Create(@event.Priority),
            consultationType: ConsultationType.Create(@event.ConsultationType),
            checkInTime: @event.CheckInTime,
            queuePosition: @event.QueuePosition,
            notes: @event.Notes
        );

        Patients.Add(patient);
        LastModifiedAt = @event.Metadata.OccurredAt;
    }

    /// <summary>
    /// Gets count of patients currently in queue.
    /// </summary>
    public int CurrentCount => Patients.Count;

    /// <summary>
    /// Returns available capacity.
    /// </summary>
    public int AvailableCapacity => MaxCapacity - CurrentCount;

    /// <summary>
    /// Checks if queue is at capacity.
    /// </summary>
    public bool IsAtCapacity => CurrentCount >= MaxCapacity;
}
