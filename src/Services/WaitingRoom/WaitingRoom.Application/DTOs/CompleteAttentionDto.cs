namespace WaitingRoom.Application.DTOs;

public sealed record CompleteAttentionDto
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string Actor { get; init; }
    public string? Outcome { get; init; }
    public string? Notes { get; init; }
}
