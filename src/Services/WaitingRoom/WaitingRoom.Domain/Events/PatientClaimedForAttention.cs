namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Domain event emitted when a clinician claims the next patient to attend.
/// </summary>
public sealed record PatientClaimedForAttention : DomainEvent
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required string ConsultationType { get; init; }
    public required DateTime ClaimedAt { get; init; }
    public string? StationId { get; init; }

    public override string EventName => nameof(PatientClaimedForAttention);

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(PatientId))
            throw new InvalidOperationException("PatientId is required");

        if (string.IsNullOrWhiteSpace(PatientName))
            throw new InvalidOperationException("PatientName is required");

        if (string.IsNullOrWhiteSpace(Priority))
            throw new InvalidOperationException("Priority is required");
    }
}
