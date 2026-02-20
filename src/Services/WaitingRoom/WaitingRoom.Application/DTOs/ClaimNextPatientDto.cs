namespace WaitingRoom.Application.DTOs;

public sealed record ClaimNextPatientDto
{
    public required string QueueId { get; init; }
    public required string Actor { get; init; }
    public string? StationId { get; init; }
}
