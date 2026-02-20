namespace WaitingRoom.Application.Commands;

public sealed record DeactivateConsultingRoomCommand
{
    public required string QueueId { get; init; }
    public required string ConsultingRoomId { get; init; }
    public required string Actor { get; init; }
    public string? CorrelationId { get; init; }
}
