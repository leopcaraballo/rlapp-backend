namespace WaitingRoom.Infrastructure.Persistence.Outbox;

using BuildingBlocks.EventSourcing;

internal sealed record OutboxMessage
{
    public Guid OutboxId { get; init; }
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    public DateTime OccurredAt { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string Status { get; init; } = OutboxStatus.Pending;
    public int Attempts { get; init; }
    public DateTime? NextAttemptAt { get; init; }
    public string? LastError { get; init; }

    public static OutboxMessage FromEvent(DomainEvent @event, string payload) =>
        new()
        {
            OutboxId = Guid.NewGuid(),
            EventId = Guid.Parse(@event.Metadata.EventId),
            EventName = @event.EventName,
            OccurredAt = @event.Metadata.OccurredAt,
            CorrelationId = @event.Metadata.CorrelationId,
            CausationId = @event.Metadata.CausationId,
            Payload = payload,
            Status = OutboxStatus.Pending,
            Attempts = 0,
            NextAttemptAt = null,
            LastError = null
        };
}

internal static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Dispatched = "Dispatched";
    public const string Failed = "Failed";
}
