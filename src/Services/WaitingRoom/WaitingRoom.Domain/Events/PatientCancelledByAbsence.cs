namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

public sealed record PatientCancelledByAbsence : DomainEvent
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required DateTime CancelledAt { get; init; }
    public required int TotalAbsences { get; init; }

    public override string EventName => nameof(PatientCancelledByAbsence);

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(PatientId))
            throw new InvalidOperationException("PatientId is required");

        if (TotalAbsences <= 0)
            throw new InvalidOperationException("TotalAbsences must be greater than 0");
    }
}
