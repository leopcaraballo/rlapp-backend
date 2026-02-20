namespace WaitingRoom.Application.Ports;

using System.Data;

/// <summary>
/// Port for Outbox persistence.
///
/// Responsibility:
/// - Persist domain events in outbox table for reliable delivery
/// - Enable implementation-agnostic outbox strategies
/// - Support atomic transactions with event store
/// - Track delivery status and retry logic
///
/// Rationale:
/// This interface ABSTRACTS the outbox pattern from EventStore.
/// EventStore should NOT know about specific outbox implementations.
///
/// Benefits:
/// - Can replace OutboxStore without modifying EventStore
/// - Supports multiple outbox strategies (Polling, Streaming, etc)
/// - Testable with in-memory implementations
///
/// Implementation can use:
/// - PostgreSQL table (polling-based)
/// - Event brokers (Kafka, RabbitMQ)
/// - File storage
/// - In-memory for testing
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Adds messages to outbox table for later dispatch.
    /// Must be called within same transaction as event save.
    /// </summary>
    /// <param name="messages">Outbox messages to persist.</param>
    /// <param name="connection">Database connection in transaction.</param>
    /// <param name="transaction">Active transaction.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// Called from EventStore.SaveAsync() within same TX.
    /// Ensures atomic persistence: events + outbox messages together.
    /// </remarks>
    Task AddAsync(
        List<OutboxMessage> messages,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets pending outbox messages ready for dispatch.
    /// </summary>
    /// <param name="batchSize">Number of messages to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of pending outbox messages.</returns>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks events as successfully dispatched to message broker.
    /// </summary>
    /// <param name="eventIds">IDs of events that were dispatched.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkDispatchedAsync(
        IEnumerable<Guid> eventIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks event dispatch as failed and schedules retry.
    /// </summary>
    /// <param name="eventIds">IDs of events that failed dispatch.</param>
    /// <param name="error">Error message explaining the failure.</param>
    /// <param name="retryAfter">Time to wait before next retry attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkFailedAsync(
        IEnumerable<Guid> eventIds,
        string error,
        TimeSpan retryAfter,
        CancellationToken cancellationToken = default);
}

