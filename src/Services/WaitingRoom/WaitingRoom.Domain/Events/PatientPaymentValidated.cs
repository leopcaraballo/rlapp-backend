namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

public sealed record PatientPaymentValidated : DomainEvent
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required string ConsultationType { get; init; }
    public required DateTime ValidatedAt { get; init; }
    public string? PaymentReference { get; init; }

    public override string EventName => nameof(PatientPaymentValidated);

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(PatientId))
            throw new InvalidOperationException("PatientId is required");

        if (string.IsNullOrWhiteSpace(PatientName))
            throw new InvalidOperationException("PatientName is required");
    }
}
