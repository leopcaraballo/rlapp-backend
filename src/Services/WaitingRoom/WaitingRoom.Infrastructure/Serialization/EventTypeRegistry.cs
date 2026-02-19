namespace WaitingRoom.Infrastructure.Serialization;

using WaitingRoom.Domain.Events;

public sealed class EventTypeRegistry
{
    private readonly Dictionary<string, Type> _byName;

    public EventTypeRegistry(IEnumerable<Type> eventTypes)
    {
        if (eventTypes == null)
            throw new ArgumentNullException(nameof(eventTypes));

        _byName = new Dictionary<string, Type>(StringComparer.Ordinal);

        foreach (var eventType in eventTypes)
        {
            if (!typeof(BuildingBlocks.EventSourcing.DomainEvent).IsAssignableFrom(eventType))
                throw new ArgumentException($"Type '{eventType.Name}' is not a DomainEvent", nameof(eventTypes));

            var name = eventType.Name;
            if (_byName.ContainsKey(name))
                throw new InvalidOperationException($"Duplicate event name '{name}' in registry");

            _byName[name] = eventType;
        }
    }

    public Type GetType(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            throw new ArgumentException("Event name is required", nameof(eventName));

        if (!_byName.TryGetValue(eventName, out var eventType))
            throw new InvalidOperationException($"Unknown event type '{eventName}'");

        return eventType;
    }

    public static EventTypeRegistry CreateDefault() =>
        new([
            typeof(PatientCheckedIn)
        ]);
}
