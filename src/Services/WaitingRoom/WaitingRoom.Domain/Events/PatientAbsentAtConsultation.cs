namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

public sealed record PatientAbsentAtConsultation : DomainEvent
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required DateTime AbsentAt { get; init; }
    public required int RetryNumber { get; init; }

    public override string EventName => nameof(PatientAbsentAtConsultation);

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(PatientId))
            throw new InvalidOperationException("PatientId is required");

        if (RetryNumber <= 0)
            throw new InvalidOperationException("RetryNumber must be greater than 0");
    }
}
