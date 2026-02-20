namespace WaitingRoom.Application.Ports;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Contract for outbox message representation.
/// Lives in Application layer so both Application and Infrastructure can reference it.
/// This follows the Ports & Adapters pattern where contracts live in Application.
/// </summary>
public sealed record OutboxMessage
{
    /// <summary>
    /// Unique outbox message identifier.
    /// </summary>
    public Guid OutboxId { get; init; }

    /// <summary>
    /// Domain event identifier.
    /// </summary>
    public Guid EventId { get; init; }

    /// <summary>
    /// Event type name (e.g., "PatientCheckedIn").
    /// </summary>
    public string EventName { get; init; } = string.Empty;

    /// <summary>
    /// When event occurred.
    /// </summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>
    /// Correlation ID for tracing.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Causation ID for command tracing.
    /// </summary>
    public string CausationId { get; init; } = string.Empty;

    /// <summary>
    /// JSON serialized event payload.
    /// </summary>
    public string Payload { get; init; } = string.Empty;

    /// <summary>
    /// Outbox status (Pending, Dispatched, Failed).
    /// </summary>
    public string Status { get; init; } = OutboxStatus.Pending;

    /// <summary>
    /// Number of delivery attempts.
    /// </summary>
    public int Attempts { get; init; }

    /// <summary>
    /// When to retry if delivery failed.
    /// </summary>
    public DateTime? NextAttemptAt { get; init; }

    /// <summary>
    /// Last error message if delivery failed.
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Creates outbox message from domain event.
    /// </summary>
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

/// <summary>
/// Constants for outbox message status values.
/// </summary>
public static class OutboxStatus
{
    public const string Pending = "Pending";
    public const string Dispatched = "Dispatched";
    public const string Failed = "Failed";
}
