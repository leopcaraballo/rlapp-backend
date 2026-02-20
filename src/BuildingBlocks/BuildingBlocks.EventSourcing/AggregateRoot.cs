namespace BuildingBlocks.EventSourcing;

/// <summary>
/// Abstract base class for aggregate roots using event sourcing.
///
/// Aggregates:
/// - Maintain invariants
/// - Generate domain events
/// - Load state from event history
/// - Track uncommitted events for persistence
///
/// The aggregate root pattern ensures:
/// - Consistency boundaries
/// - Invariant protection
/// - Deterministic event sourcing
/// </summary>
public abstract class AggregateRoot
{
    /// <summary>
    /// Unique identifier for this aggregate.
    /// </summary>
    public string Id { get; protected set; } = string.Empty;

    /// <summary>
    /// Current version of the aggregate.
    /// Incremented with each event.
    /// </summary>
    public long Version { get; protected set; }

    /// <summary>
    /// Uncommitted domain events that need to be persisted.
    /// </summary>
    private readonly List<DomainEvent> _uncommittedEvents = [];

    /// <summary>
    /// Gets all uncommitted events.
    /// </summary>
    public IReadOnlyList<DomainEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    /// <summary>
    /// Loads aggregate state from complete event history.
    /// Replays all events to reconstruct current state.
    /// </summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="events">All events in order for this aggregate.</param>
    /// <returns>Aggregate with state reconstructed from events.</returns>
    public static T LoadFromHistory<T>(string id, IEnumerable<DomainEvent> events)
        where T : AggregateRoot
    {
        var aggregate = (T?)Activator.CreateInstance(typeof(T), nonPublic: true);

        if (aggregate == null)
            throw new InvalidOperationException($"Unable to create aggregate of type '{typeof(T).Name}'");

        aggregate.Id = id;
        aggregate.Version = 0;

        foreach (var @event in events)
        {
            aggregate.ApplyEvent(@event, isNew: false);
        }

        return aggregate;
    }

    /// <summary>
    /// Raises a new domain event.
    /// The event is applied to state and marked as uncommitted.
    /// </summary>
    /// <param name="event">The domain event to raise.</param>
    protected virtual void RaiseEvent(DomainEvent @event)
    {
        var appliedEvent = ApplyEvent(@event, isNew: true);
        _uncommittedEvents.Add(appliedEvent);
    }

    /// <summary>
    /// Applies an event to the aggregate's state.
    /// Uses reflection to find and invoke When() method matching event type.
    /// </summary>
    /// <param name="event">The event to apply.</param>
    /// <param name="isNew">Whether this is a new event or from history.</param>
    private DomainEvent ApplyEvent(DomainEvent @event, bool isNew)
    {
        // Dynamic dispatch: find When() method for this event type
        var whenMethod = GetType()
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == "When" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == @event.GetType());

        if (whenMethod == null)
            throw new InvalidOperationException(
                $"No When() method found for event type {{{@event.GetType().Name}}} on aggregate {{{GetType().Name}}}");

        // Invoke When() to apply event to state
        whenMethod.Invoke(this, [@event]);

        // Update version for new events from domain operations
        if (isNew)
        {
            Version++;
            @event = @event with { Metadata = @event.Metadata.WithVersion(Version) };
        }
        else
        {
            // For replayed events, trust the version from event
            if (@event.Metadata.Version > Version)
                Version = @event.Metadata.Version;
        }

        return @event;
    }

    /// <summary>
    /// Clears uncommitted events after successful persistence.
    /// Called by infrastructure after event store save.
    /// </summary>
    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    /// <summary>
    /// Returns whether the aggregate has any uncommitted changes.
    /// </summary>
    public bool HasUncommittedEvents => _uncommittedEvents.Count > 0;
}
