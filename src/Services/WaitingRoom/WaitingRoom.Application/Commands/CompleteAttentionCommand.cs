namespace WaitingRoom.Application.Commands;

public sealed record CompleteAttentionCommand
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string Actor { get; init; }
    public string? Outcome { get; init; }
    public string? Notes { get; init; }
    public string? CorrelationId { get; init; }
}
