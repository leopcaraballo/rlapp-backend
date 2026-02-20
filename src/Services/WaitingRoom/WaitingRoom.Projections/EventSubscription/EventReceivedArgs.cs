namespace WaitingRoom.Projections.EventSubscription;

using BuildingBlocks.EventSourcing;
using System;

/// <summary>
/// Event args for received events.
/// </summary>
public sealed class EventReceivedArgs : EventArgs
{
    public required DomainEvent Event { get; init; }
    public required DateTime ReceivedAt { get; init; }
    public required string RoutingKey { get; init; }
}
