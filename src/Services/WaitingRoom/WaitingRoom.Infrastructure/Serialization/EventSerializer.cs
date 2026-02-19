namespace WaitingRoom.Infrastructure.Serialization;

using BuildingBlocks.EventSourcing;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

public sealed class EventSerializer
{
    private readonly EventTypeRegistry _registry;
    private readonly JsonSerializerSettings _settings;

    public EventSerializer(EventTypeRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    public string Serialize(DomainEvent @event)
    {
        if (@event == null)
            throw new ArgumentNullException(nameof(@event));

        return JsonConvert.SerializeObject(@event, _settings);
    }

    public DomainEvent Deserialize(string eventName, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload is required", nameof(payload));

        var eventType = _registry.GetType(eventName);
        var deserialized = JsonConvert.DeserializeObject(payload, eventType, _settings);

        if (deserialized is not DomainEvent domainEvent)
            throw new InvalidOperationException($"Failed to deserialize event '{eventName}'");

        return domainEvent;
    }
}
