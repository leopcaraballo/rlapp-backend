namespace WaitingRoom.Application.Commands;

public sealed record CancelByPaymentCommand
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string Actor { get; init; }
    public string? Reason { get; init; }
    public string? CorrelationId { get; init; }
}
