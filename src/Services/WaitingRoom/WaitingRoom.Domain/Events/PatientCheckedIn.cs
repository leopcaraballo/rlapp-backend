namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;
using ValueObjects;

/// <summary>
/// Domain event representing the fact that a patient has checked into the waiting room.
///
/// This event:
/// - Is immutable (record type)
/// - Represents a past fact ("CheckedIn")
/// - Contains all data needed to reconstruct queue state
/// - Includes metadata for tracing and consistency
/// - Can be replayed deterministically
/// </summary>
public sealed record PatientCheckedIn : DomainEvent
{
    /// <summary>
    /// Queue where patient checked in.
    /// </summary>
    public required string QueueId { get; init; }

    /// <summary>
    /// Patient identifier.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// Patient full name.
    /// </summary>
    public required string PatientName { get; init; }

    /// <summary>
    /// Priority level assigned.
    /// </summary>
    public required string Priority { get; init; }

    /// <summary>
    /// Type of consultation requested.
    /// </summary>
    public required string ConsultationType { get; init; }

    /// <summary>
    /// Optional notes about the patient or consultation.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Position in queue (0-based).
    /// </summary>
    public required int QueuePosition { get; init; }

    /// <summary>
    /// Timestamp when check-in occurred.
    /// </summary>
    public required DateTime CheckInTime { get; init; }

    public override string EventName => "PatientCheckedIn";

    /// <summary>
    /// Validates the semantic rules of the event.
    /// </summary>
    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(PatientId))
            throw new InvalidOperationException("PatientId is required");

        if (string.IsNullOrWhiteSpace(PatientName))
            throw new InvalidOperationException("PatientName is required");

        if (string.IsNullOrWhiteSpace(Priority))
            throw new InvalidOperationException("Priority is required");

        if (string.IsNullOrWhiteSpace(ConsultationType))
            throw new InvalidOperationException("ConsultationType is required");

        if (QueuePosition < 0)
            throw new InvalidOperationException("QueuePosition cannot be negative");
    }
}
