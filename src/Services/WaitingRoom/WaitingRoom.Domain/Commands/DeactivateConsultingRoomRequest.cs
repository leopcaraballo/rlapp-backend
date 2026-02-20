namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;

public sealed record DeactivateConsultingRoomRequest
{
    public required string ConsultingRoomId { get; init; }
    public required DateTime DeactivatedAt { get; init; }
    public required EventMetadata Metadata { get; init; }
}
