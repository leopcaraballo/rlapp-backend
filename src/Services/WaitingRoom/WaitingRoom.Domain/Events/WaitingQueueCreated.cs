namespace WaitingRoom.Domain.Events;

using BuildingBlocks.EventSourcing;

/// <summary>
/// Domain event representing creation of a waiting queue.
/// </summary>
public sealed record WaitingQueueCreated : DomainEvent
{
    /// <summary>
    /// Queue unique identifier.
    /// </summary>
    public required string QueueId { get; init; }

    /// <summary>
    /// Human-readable queue name.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// Maximum capacity of the queue.
    /// </summary>
    public required int MaxCapacity { get; init; }

    /// <summary>
    /// When the queue was created.
    /// </summary>
    public required DateTime CreatedAt { get; init; }

    public override string EventName => "WaitingQueueCreated";

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();

        if (string.IsNullOrWhiteSpace(QueueId))
            throw new InvalidOperationException("QueueId is required");

        if (string.IsNullOrWhiteSpace(QueueName))
            throw new InvalidOperationException("QueueName is required");

        if (MaxCapacity <= 0)
            throw new InvalidOperationException("MaxCapacity must be greater than 0");
    }
}
