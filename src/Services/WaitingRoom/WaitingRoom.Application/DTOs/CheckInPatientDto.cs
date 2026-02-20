namespace WaitingRoom.Application.DTOs;

/// <summary>
/// DTO for CheckInPatient command request.
///
/// This DTO:
/// - Transfers data from API/external boundaries
/// - Is NOT a domain object
/// - Contains only values needed for the use case
/// - Should be validated at the boundary (API layer)
///
/// Flow:
/// HTTP Request → Controller → CheckInPatientDto → CheckInPatientCommand → Handler
/// </summary>
public sealed record CheckInPatientDto
{
    /// <summary>
    /// Unique queue identifier.
    /// </summary>
    public required string QueueId { get; init; }

    /// <summary>
    /// Unique patient identifier.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// Patient full name.
    /// </summary>
    public required string PatientName { get; init; }

    /// <summary>
    /// Priority level (e.g., "High", "Medium", "Low").
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
    /// Optional notes about patient or consultation.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// Actor performing check-in (e.g., nurse ID or system name).
    /// </summary>
    public required string Actor { get; init; }
}
