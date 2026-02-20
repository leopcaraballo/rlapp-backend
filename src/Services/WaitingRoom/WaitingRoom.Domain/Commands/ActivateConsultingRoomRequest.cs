namespace WaitingRoom.Domain.Commands;

using BuildingBlocks.EventSourcing;

public sealed record ActivateConsultingRoomRequest
{
    public required string ConsultingRoomId { get; init; }
    public required DateTime ActivatedAt { get; init; }
    public required EventMetadata Metadata { get; init; }
}
