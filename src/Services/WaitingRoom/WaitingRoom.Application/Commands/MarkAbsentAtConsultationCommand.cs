namespace WaitingRoom.Application.Commands;

public sealed record MarkAbsentAtConsultationCommand
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string Actor { get; init; }
    public string? CorrelationId { get; init; }
}
