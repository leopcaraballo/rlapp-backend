namespace WaitingRoom.Application.DTOs;

/// <summary>
/// DTO representing a patient in the waiting queue.
/// Part of WaitingQueueDto response.
/// </summary>
public sealed record PatientInQueueDto
{
    /// <summary>
    /// Patient identifier.
    /// </summary>
    public required string PatientId { get; init; }

    /// <summary>
    /// Patient full name.
    /// </summary>
    public required string PatientName { get; init; }

    /// <summary>
    /// Priority level.
    /// </summary>
    public required string Priority { get; init; }

    /// <summary>
    /// Type of consultation.
    /// </summary>
    public required string ConsultationType { get; init; }

    /// <summary>
    /// When patient checked in.
    /// </summary>
    public required DateTime CheckedInAt { get; init; }

    /// <summary>
    /// Position in queue (0-based).
    /// </summary>
    public required int Position { get; init; }

    /// <summary>
    /// Optional consultation notes.
    /// </summary>
    public string? Notes { get; init; }
}
