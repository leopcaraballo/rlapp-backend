namespace WaitingRoom.Application.Ports;

using System.Data;
using WaitingRoom.Infrastructure.Persistence.Outbox;

/// <summary>
/// Port for Outbox persistence.
///
/// Responsibility:
/// - Persist domain events in outbox table for reliable delivery
/// - Enable implementation-agnostic outbox strategies
/// - Support atomic transactions with event store
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
}
