namespace WaitingRoom.Infrastructure.Persistence.Outbox;

/// <summary>
/// Port for Outbox message storage.
///
/// Responsibilities:
/// - Store pending outbox messages
/// - Retrieve pending messages for dispatch
/// - Update message status (dispatched/failed)
///
/// Implementation:
/// - PostgresOutboxStore uses PostgreSQL
/// - Test implementations can use in-memory storage
/// </summary>
internal interface IOutboxStore
{
    /// <summary>
    /// Retrieves pending outbox messages ready for dispatch.
    /// Respects next_attempt_at for retry scheduling.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks messages as successfully dispatched.
    /// </summary>
    Task MarkDispatchedAsync(
        IEnumerable<Guid> eventIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks messages as failed with retry information.
    /// </summary>
    Task MarkFailedAsync(
        IEnumerable<Guid> eventIds,
        string error,
        TimeSpan retryAfter,
        CancellationToken cancellationToken = default);
}
