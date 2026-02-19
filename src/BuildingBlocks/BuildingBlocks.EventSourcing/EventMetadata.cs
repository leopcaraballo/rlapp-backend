namespace BuildingBlocks.EventSourcing;

/// <summary>
/// Metadata associated with every domain event for traceability and consistency.
/// Immutable record containing correlation IDs, causation chain, actor info, and idempotency keys.
/// </summary>
public sealed record EventMetadata
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    public string EventId { get; init; } = string.Empty;

    /// <summary>
    /// Aggregate root identifier that generated this event.
    /// </summary>
    public string AggregateId { get; init; } = string.Empty;

    /// <summary>
    /// Version of the aggregate after this event.
    /// </summary>
    public long Version { get; init; }

    /// <summary>
    /// UTC timestamp when the event occurred.
    /// </summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>
    /// Correlation ID for distributed tracing across service boundaries.
    /// Allows tracking requests from originating command through all resulting events.
    /// </summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Causation ID - the event or command that caused this event.
    /// Allows reconstructing the causal chain.
    /// </summary>
    public string CausationId { get; init; } = string.Empty;

    /// <summary>
    /// Actor that triggered this event (user ID, system, etc).
    /// </summary>
    public string Actor { get; init; } = string.Empty;

    /// <summary>
    /// Idempotency key - prevents duplicate event processing.
    /// If same command is retried with same idempotency key, duplicate events are prevented.
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// Schema version of the event for evolution purposes.
    /// Allows handling multiple versions of the same event type.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Creates new metadata for a domain event.
    /// </summary>
    /// <param name="aggregateId">ID of the aggregate generating the event.</param>
    /// <param name="actor">User or system generating the event.</param>
    /// <param name="correlationId">Correlation ID for distributed tracing. If not provided, a new guid is generated.</param>
    /// <param name="causationId">ID of the command or event causing this event. If not provided, defaults to EventId.</param>
    /// <returns>New EventMetadata with all traceability fields populated.</returns>
    public static EventMetadata CreateNew(
        string aggregateId,
        string actor,
        string? correlationId = null,
        string? causationId = null)
    {
        var eventId = Guid.NewGuid().ToString("D");

        return new()
        {
            EventId = eventId,
            AggregateId = aggregateId,
            Version = 1, // Will be set by event store
            OccurredAt = DateTime.UtcNow,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString("D"),
            CausationId = causationId ?? eventId,
            Actor = actor,
            IdempotencyKey = Guid.NewGuid().ToString("D"),
            SchemaVersion = 1
        };
    }

    /// <summary>
    /// Creates metadata for a new version of an existing event.
    /// Used when replaying events with updated schema.
    /// </summary>
    public EventMetadata WithVersion(long version) =>
        this with { Version = version };

    /// <summary>
    /// Updates the idempotency key (used for command retries).
    /// </summary>
    public EventMetadata WithIdempotencyKey(string key) =>
        this with { IdempotencyKey = key };
}
