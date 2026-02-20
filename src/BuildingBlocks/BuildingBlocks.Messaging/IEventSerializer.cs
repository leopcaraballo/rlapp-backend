namespace BuildingBlocks.Messaging;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Abstraction for event serialization.
/// Allows projections and infrastructure to share contracts without direct dependency.
/// </summary>
public interface IEventSerializer
{
    string Serialize(DomainEvent @event);

    DomainEvent Deserialize(string eventName, string payload);
}
