namespace WaitingRoom.Infrastructure.Persistence.EventStore;

internal static class EventStoreSchema
{
    public const string EventsTable = "waiting_room_events";
    public const string OutboxTable = "waiting_room_outbox";

    public const string CreateEventsTableSql = @"
CREATE TABLE IF NOT EXISTS waiting_room_events (
    event_id UUID PRIMARY KEY,
    aggregate_id TEXT NOT NULL,
    version BIGINT NOT NULL,
    event_name TEXT NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    correlation_id TEXT NOT NULL,
    causation_id TEXT NOT NULL,
    actor TEXT NOT NULL,
    idempotency_key TEXT NOT NULL,
    schema_version INT NOT NULL,
    payload JSONB NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_waiting_room_events_aggregate_version
    ON waiting_room_events (aggregate_id, version);

CREATE UNIQUE INDEX IF NOT EXISTS ux_waiting_room_events_idempotency
    ON waiting_room_events (idempotency_key);
";

    public const string CreateOutboxTableSql = @"
CREATE TABLE IF NOT EXISTS waiting_room_outbox (
    outbox_id UUID PRIMARY KEY,
    event_id UUID NOT NULL,
    event_name TEXT NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL,
    correlation_id TEXT NOT NULL,
    causation_id TEXT NOT NULL,
    payload JSONB NOT NULL,
    status TEXT NOT NULL,
    attempts INT NOT NULL,
    next_attempt_at TIMESTAMPTZ NULL,
    last_error TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_waiting_room_outbox_event
    ON waiting_room_outbox (event_id);

CREATE INDEX IF NOT EXISTS ix_waiting_room_outbox_pending
    ON waiting_room_outbox (status, next_attempt_at);
";
}
