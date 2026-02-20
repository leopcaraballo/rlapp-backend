namespace WaitingRoom.Infrastructure.Messaging;

using BuildingBlocks.EventSourcing;
using WaitingRoom.Application.Ports;

/// <summary>
/// No-op publisher used when the outbox worker is the sole publisher.
/// Ensures commands only persist events and the worker handles dispatch.
/// </summary>
internal sealed class OutboxEventPublisher : IEventPublisher
{
    public Task PublishAsync(IEnumerable<DomainEvent> events, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishAsync(DomainEvent @event, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
