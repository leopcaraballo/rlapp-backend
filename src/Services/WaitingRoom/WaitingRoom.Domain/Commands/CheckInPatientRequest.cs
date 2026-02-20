namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Domain.ValueObjects;

/// <summary>
/// Value Object representing the complete check-in request.
///
/// Purpose:
/// - Encapsulate all parameters for CheckInPatient operation
/// - Reduce parameter count from 7 to 1 (Parameter Object pattern)
/// - Improve testability (single object to construct)
/// - Support future extensions without signature changes
///
/// This violates the anti-pattern of "parameter cascading" where
/// a method has too many parameters. Instead, we use a single
/// request object that groups related data.
///
/// Pattern: Command Object + Parameter Object
/// </summary>
public sealed record CheckInPatientRequest
{
    /// <summary>
    /// Unique patient identifier.
    /// </summary>
public required PatientId PatientId { get; init; }

/// <summary>
/// Patient name for display.
/// </summary>
public required string PatientName { get; init; }

/// <summary>
/// Queue priority (Low, Medium, High, Urgent).
/// </summary>
public required Priority Priority { get; init; }

/// <summary>
/// Type of consultation (e.g., Cardiology, Dentistry).
/// </summary>
public required ConsultationType ConsultationType { get; init; }

/// <summary>
/// When patient checked in to the queue.
/// </summary>
public required DateTime CheckInTime { get; init; }

/// <summary>
/// Event metadata for traceability and idempotency.
/// </summary>
public required EventMetadata Metadata { get; init; }
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Validates all required properties are set.
    /// Ensures request is valid before domain operation.
    /// </summary>
    public bool IsValid()
    {
        return PatientId != null &&
               !string.IsNullOrWhiteSpace(PatientName) &&
               Priority != null &&
               ConsultationType != null &&
               Metadata != null;
    }
}
