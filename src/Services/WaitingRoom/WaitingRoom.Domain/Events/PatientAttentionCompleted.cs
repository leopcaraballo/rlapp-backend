namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Domain event emitted when patient attention is completed.
/// </summary>
public sealed record PatientAttentionCompleted : DomainEvent
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required string ConsultationType { get; init; }
    public required DateTime CompletedAt { get; init; }
    public string? Outcome { get; init; }
    public string? Notes { get; init; }

    public override string EventName => nameof(PatientAttentionCompleted);

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
