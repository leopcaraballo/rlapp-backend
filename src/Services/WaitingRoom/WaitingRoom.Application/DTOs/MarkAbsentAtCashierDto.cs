namespace WaitingRoom.Application.DTOs;

public sealed record MarkAbsentAtCashierDto
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string Actor { get; init; }
}
