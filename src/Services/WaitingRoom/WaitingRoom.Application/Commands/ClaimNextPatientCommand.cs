namespace WaitingRoom.Application.Commands;

public sealed record ClaimNextPatientCommand
{
    public required string QueueId { get; init; }
    public required string Actor { get; init; }
    public string? CorrelationId { get; init; }
    public string? StationId { get; init; }
}
