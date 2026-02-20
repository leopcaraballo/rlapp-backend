namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

public sealed record ConsultingRoomActivated : DomainEvent
{
    public required string QueueId { get; init; }
    public required string ConsultingRoomId { get; init; }
    public required DateTime ActivatedAt { get; init; }

    public override string EventName => nameof(ConsultingRoomActivated);

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(ConsultingRoomId))
            throw new InvalidOperationException("ConsultingRoomId is required");
    }
}
