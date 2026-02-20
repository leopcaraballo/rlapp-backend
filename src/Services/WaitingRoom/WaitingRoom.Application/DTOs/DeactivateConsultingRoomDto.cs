namespace WaitingRoom.Application.DTOs;

public sealed record DeactivateConsultingRoomDto
{
    public required string QueueId { get; init; }
    public required string ConsultingRoomId { get; init; }
    public required string Actor { get; init; }
}
