namespace BuildingBlocks.EventSourcing;

/// <summary>
/// Base class for all domain events.
///
/// Events represent facts that have happened in the domain.
/// They are immutable and form the source of truth for the system.
///
/// Key invariants:
/// - Events are immutable (no mutation after creation)
/// - Events represent past tense (something that already happened)
/// - Events contain all data needed to reconstruct aggregate state
/// - Events are versionable (schema can evolve)
/// - Events are idempotent (same event broadcast twice = same final state)
/// </summary>
public abstract record DomainEvent
{
    /// <summary>
    /// Core metadata for tracing and consistency.
    /// </summary>
    public required EventMetadata Metadata { get; init; }

    /// <summary>
    /// Gets the event name (type name used in event store).
    /// </summary>
    public virtual string EventName => GetType().Name;

    /// <summary>
    /// Validates event invariants.
    /// Subclasses should override to validate semantic rules.
    /// </summary>
    /// <exception cref="InvalidOperationException">If event violates invariants.</exception>
    protected virtual void ValidateInvariants()
    {
        if (string.IsNullOrWhiteSpace(Metadata.AggregateId))
            throw new InvalidOperationException("Event must have an AggregateId");

        if (string.IsNullOrWhiteSpace(Metadata.CorrelationId))
            throw new InvalidOperationException("Event must have a CorrelationId");

        if (string.IsNullOrWhiteSpace(Metadata.Actor))
            throw new InvalidOperationException("Event must have an Actor");
    }

    /// <summary>
    /// Serialize event to JSON-safe dictionary.
    /// Used for persistence.
    /// </summary>
    public virtual Dictionary<string, object> ToPayload() =>
        new()
        {
            { "aggregateId", Metadata.AggregateId },
            { "version", Metadata.Version },
            { "occurredAt", Metadata.OccurredAt },
            { "correlationId", Metadata.CorrelationId },
            { "causationId", Metadata.CausationId },
            { "actor", Metadata.Actor },
            { "idempotencyKey", Metadata.IdempotencyKey },
            { "schemaVersion", Metadata.SchemaVersion }
        };
}
