namespace WaitingRoom.Infrastructure.Persistence.EventStore;

using System.Data;
using BuildingBlocks.EventSourcing;
using Dapper;
using Npgsql;
using WaitingRoom.Application.Exceptions;
using WaitingRoom.Application.Ports;
using WaitingRoom.Domain.Aggregates;
using WaitingRoom.Infrastructure.Persistence.Outbox;
using WaitingRoom.Infrastructure.Serialization;

internal sealed class PostgresEventStore : IEventStore
{
    private readonly string _connectionString;
    private readonly EventSerializer _serializer;
    private readonly PostgresOutboxStore _outboxStore;

    public PostgresEventStore(
        string connectionString,
        EventSerializer serializer,
        PostgresOutboxStore outboxStore)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required", nameof(connectionString));

        _connectionString = connectionString;
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _outboxStore = outboxStore ?? throw new ArgumentNullException(nameof(outboxStore));
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            EventStoreSchema.CreateEventsTableSql,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            EventStoreSchema.CreateOutboxTableSql,
            cancellationToken: cancellationToken));
    }

    public async Task<IEnumerable<DomainEvent>> GetEventsAsync(
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT event_name AS EventName, payload AS Payload
FROM waiting_room_events
WHERE aggregate_id = @AggregateId
ORDER BY version;";

        await using var connection = new NpgsqlConnection(_connectionString);
        var command = new CommandDefinition(
            sql,
            new { AggregateId = aggregateId },
            cancellationToken: cancellationToken);

        var rows = await connection.QueryAsync<EventRow>(command);
        return rows.Select(row => _serializer.Deserialize(row.EventName, row.Payload)).ToList();
    }

    public async Task<WaitingQueue?> LoadAsync(
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        var events = (await GetEventsAsync(aggregateId, cancellationToken)).ToList();

        if (events.Count == 0)
            return null;

        return AggregateRoot.LoadFromHistory<WaitingQueue>(aggregateId, events);
    }

    public async Task SaveAsync(
        WaitingQueue aggregate,
        CancellationToken cancellationToken = default)
    {
        if (aggregate == null)
            throw new ArgumentNullException(nameof(aggregate));

        if (!aggregate.HasUncommittedEvents)
            return;

        var uncommitted = aggregate.UncommittedEvents.ToList();
        var expectedVersion = aggregate.Version - uncommitted.Count;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var currentVersion = await GetCurrentVersionAsync(
            connection,
            transaction,
            aggregate.Id,
            cancellationToken);

        if (currentVersion != expectedVersion)
            throw new EventConflictException(aggregate.Id, expectedVersion, currentVersion);

        const string insertSql = @"
INSERT INTO waiting_room_events (
    event_id,
    aggregate_id,
    version,
    event_name,
    occurred_at,
    correlation_id,
    causation_id,
    actor,
    idempotency_key,
    schema_version,
    payload
)
VALUES (
    @EventId,
    @AggregateId,
    @Version,
    @EventName,
    @OccurredAt,
    @CorrelationId,
    @CausationId,
    @Actor,
    @IdempotencyKey,
    @SchemaVersion,
    CAST(@Payload AS JSONB)
)
ON CONFLICT (idempotency_key) DO NOTHING;
";

        var outboxMessages = new List<OutboxMessage>();
        var index = 0;

        foreach (var @event in uncommitted)
        {
            var version = expectedVersion + (++index);
            var updatedEvent = @event with { Metadata = @event.Metadata.WithVersion(version) };
            var payload = _serializer.Serialize(updatedEvent);

            var inserted = await connection.ExecuteAsync(new CommandDefinition(
                insertSql,
                new
                {
                    EventId = Guid.Parse(updatedEvent.Metadata.EventId),
                    AggregateId = updatedEvent.Metadata.AggregateId,
                    Version = updatedEvent.Metadata.Version,
                    EventName = updatedEvent.EventName,
                    OccurredAt = updatedEvent.Metadata.OccurredAt,
                    CorrelationId = updatedEvent.Metadata.CorrelationId,
                    CausationId = updatedEvent.Metadata.CausationId,
                    Actor = updatedEvent.Metadata.Actor,
                    IdempotencyKey = updatedEvent.Metadata.IdempotencyKey,
                    SchemaVersion = updatedEvent.Metadata.SchemaVersion,
                    Payload = payload
                },
                transaction,
                cancellationToken: cancellationToken));

            if (inserted > 0)
                outboxMessages.Add(OutboxMessage.FromEvent(updatedEvent, payload));
        }

        if (outboxMessages.Count > 0)
        {
            await _outboxStore.AddAsync(outboxMessages, connection, transaction, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        aggregate.ClearUncommittedEvents();
    }

    private static async Task<long> GetCurrentVersionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string aggregateId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT COALESCE(MAX(version), 0)
FROM waiting_room_events
WHERE aggregate_id = @AggregateId;";

        var command = new CommandDefinition(
            sql,
            new { AggregateId = aggregateId },
            transaction,
            cancellationToken: cancellationToken);

        return await connection.ExecuteScalarAsync<long>(command);
    }

    private sealed class EventRow
    {
        public string EventName { get; init; } = string.Empty;
        public string Payload { get; init; } = string.Empty;
    }
}
