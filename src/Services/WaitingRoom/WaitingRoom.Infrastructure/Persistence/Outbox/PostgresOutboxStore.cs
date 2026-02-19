namespace WaitingRoom.Infrastructure.Persistence.Outbox;

using System.Data;
using Dapper;
using Npgsql;
using WaitingRoom.Infrastructure.Persistence.EventStore;

internal sealed class PostgresOutboxStore
{
    private readonly string _connectionString;

    public PostgresOutboxStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required", nameof(connectionString));

        _connectionString = connectionString;
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            EventStoreSchema.CreateOutboxTableSql,
            cancellationToken: cancellationToken));
    }

    public async Task AddAsync(
        IEnumerable<OutboxMessage> messages,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
INSERT INTO waiting_room_outbox (
    outbox_id,
    event_id,
    event_name,
    occurred_at,
    correlation_id,
    causation_id,
    payload,
    status,
    attempts,
    next_attempt_at,
    last_error
)
VALUES (
    @OutboxId,
    @EventId,
    @EventName,
    @OccurredAt,
    @CorrelationId,
    @CausationId,
    CAST(@Payload AS JSONB),
    @Status,
    @Attempts,
    @NextAttemptAt,
    @LastError
)
ON CONFLICT (event_id) DO NOTHING;
";

        var command = new CommandDefinition(sql, messages, transaction, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(command);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT
    outbox_id AS OutboxId,
    event_id AS EventId,
    event_name AS EventName,
    occurred_at AS OccurredAt,
    correlation_id AS CorrelationId,
    causation_id AS CausationId,
    payload AS Payload,
    status AS Status,
    attempts AS Attempts,
    next_attempt_at AS NextAttemptAt,
    last_error AS LastError
FROM waiting_room_outbox
WHERE status = @Status
  AND (next_attempt_at IS NULL OR next_attempt_at <= NOW())
ORDER BY occurred_at
LIMIT @BatchSize;
";

        await using var connection = new NpgsqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { Status = OutboxStatus.Pending, BatchSize = batchSize },
            cancellationToken: cancellationToken);

        var results = await connection.QueryAsync<OutboxMessage>(command);
        return results.ToList();
    }

    public async Task MarkDispatchedAsync(
        IEnumerable<Guid> eventIds,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
UPDATE waiting_room_outbox
SET status = @Status,
    attempts = attempts + 1,
    next_attempt_at = NULL,
    last_error = NULL
WHERE event_id = ANY(@EventIds);
";

        await using var connection = new NpgsqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { Status = OutboxStatus.Dispatched, EventIds = eventIds.ToArray() },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }

    public async Task MarkFailedAsync(
        IEnumerable<Guid> eventIds,
        string error,
        TimeSpan retryAfter,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
UPDATE waiting_room_outbox
SET status = @Status,
    attempts = attempts + 1,
    next_attempt_at = NOW() + @RetryAfter,
    last_error = @Error
WHERE event_id = ANY(@EventIds);
";

        await using var connection = new NpgsqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new
            {
                Status = OutboxStatus.Failed,
                EventIds = eventIds.ToArray(),
                Error = error,
                RetryAfter = retryAfter
            },
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
