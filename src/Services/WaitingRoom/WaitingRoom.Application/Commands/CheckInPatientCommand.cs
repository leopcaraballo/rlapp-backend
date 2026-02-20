namespace WaitingRoom.Application.Commands;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Command to check in a patient into a waiting queue.
///
/// This command:
/// - Represents an intent to check in a patient
/// - Contains all data needed to execute the operation
/// - Is immutable (record type)
/// - Is NOT the event (event comes AFTER execution)
///
/// Difference from Event:
/// - Command = intent/request (future tense)
/// - Event = fact/result (past tense)
///
/// Flow:
/// Controller → Command → Handler → Aggregate → Event → EventStore → Projection
/// </summary>
public sealed record CheckInPatientCommand
{
    /// <summary>
    /// Unique identifier for the waiting queue.
    /// </summary>
    public required string QueueId { get; init; }

    /// <summary>
    /// Unique identifier for the patient.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// Patient full name.
    /// </summary>
    public required string PatientName { get; init; }

    /// <summary>
    /// Priority level for consultation (e.g., "High", "Medium", "Low").
    /// </summary>
    public required string Priority { get; init; }

    /// <summary>
    /// Type of consultation (e.g., "General", "Dental", "Surgery").
    /// </summary>
    public required string ConsultationType { get; init; }

    /// <summary>
    /// Optional patient age for automatic administrative priority.
    /// </summary>
    public int? Age { get; init; }

    /// <summary>
    /// Optional pregnancy flag for automatic administrative priority.
    /// </summary>
    public bool? IsPregnant { get; init; }

    /// <summary>
    /// Optional medical notes about the patient or consultation.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// User or system performing the check-in (for audit trail).
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>
    /// Optional correlation ID for distributed tracing.
    /// If not provided, a new one is generated.
    /// </summary>
    public string? CorrelationId { get; init; }
}
