-- =============================================================================
-- RLAPP PostgreSQL Schema Initialization
-- Event Store, Outbox, and Read Models
-- =============================================================================

-- Create databases
CREATE DATABASE rlapp_waitingroom;
CREATE DATABASE rlapp_waitingroom_read;
CREATE DATABASE rlapp_waitingroom_test;

-- Connect to eventstore database
\c rlapp_waitingroom

-- =============================================================================
-- EVENT STORE SCHEMA
-- =============================================================================

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

-- =============================================================================
-- OUTBOX PATTERN TABLE
-- =============================================================================

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

-- =============================================================================
-- LAG MONITORING TABLE (Observability)
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_processing_lag (
    event_id UUID PRIMARY KEY,
    event_name TEXT NOT NULL,
    aggregate_id TEXT NOT NULL,
    event_created_at TIMESTAMPTZ NOT NULL,
    event_published_at TIMESTAMPTZ,
    projection_processed_at TIMESTAMPTZ,
    outbox_dispatch_duration_ms INT,
    projection_processing_duration_ms INT,
    total_lag_ms INT,
    status TEXT NOT NULL, -- CREATED, PUBLISHED, PROCESSED, FAILED
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_event_name_lag ON event_processing_lag(event_name);
CREATE INDEX IF NOT EXISTS idx_status_lag ON event_processing_lag(status);
CREATE INDEX IF NOT EXISTS idx_created_at_lag ON event_processing_lag(created_at);

-- =============================================================================
-- PROJECTION STATE (for rebuild capability)
-- =============================================================================

CREATE TABLE IF NOT EXISTS projection_checkpoints (
    projection_id TEXT PRIMARY KEY,
    last_event_version BIGINT NOT NULL DEFAULT 0,
    checkpointed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    idempotency_key TEXT NOT NULL,
    status TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_projection_checkpoints_idempotency
    ON projection_checkpoints (idempotency_key);

-- Connect to test database
\c rlapp_waitingroom_test

-- =============================================================================
-- EVENT STORE SCHEMA (TEST)
-- =============================================================================

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

-- =============================================================================
-- OUTBOX PATTERN TABLE (TEST)
-- =============================================================================

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

-- =============================================================================
-- LAG MONITORING TABLE (TEST)
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_processing_lag (
    event_id UUID PRIMARY KEY,
    event_name TEXT NOT NULL,
    aggregate_id TEXT NOT NULL,
    event_created_at TIMESTAMPTZ NOT NULL,
    event_published_at TIMESTAMPTZ,
    projection_processed_at TIMESTAMPTZ,
    outbox_dispatch_duration_ms INT,
    projection_processing_duration_ms INT,
    total_lag_ms INT,
    status TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_event_name_lag ON event_processing_lag(event_name);
CREATE INDEX IF NOT EXISTS idx_status_lag ON event_processing_lag(status);
CREATE INDEX IF NOT EXISTS idx_created_at_lag ON event_processing_lag(created_at);

-- =============================================================================
-- PROJECTION STATE (TEST)
-- =============================================================================

CREATE TABLE IF NOT EXISTS projection_checkpoints (
    projection_id TEXT PRIMARY KEY,
    last_event_version BIGINT NOT NULL DEFAULT 0,
    checkpointed_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    idempotency_key TEXT NOT NULL,
    status TEXT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_projection_checkpoints_idempotency
    ON projection_checkpoints (idempotency_key);

-- Connect to read_models database
\c rlapp_waitingroom_read

-- =============================================================================
-- READ MODELS / PROJECTIONS
-- =============================================================================

-- Waiting Queue View (Read Model)
CREATE TABLE IF NOT EXISTS waiting_queue_view (
    queue_id TEXT PRIMARY KEY,
    queue_name VARCHAR(255) NOT NULL,
    max_capacity INT NOT NULL,
    current_patient_count INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL,
    last_modified_at TIMESTAMP NOT NULL,
    updated_by_event_version BIGINT NOT NULL
);

-- Waiting Patients View (Read Model)
CREATE TABLE IF NOT EXISTS waiting_patients_view (
    queue_id TEXT NOT NULL,
    patient_id TEXT NOT NULL,
    patient_name VARCHAR(255) NOT NULL,
    priority VARCHAR(50) NOT NULL,
    consultation_type VARCHAR(255) NOT NULL,
    notes TEXT,
    position_in_queue INT NOT NULL,
    checked_in_at TIMESTAMP NOT NULL,
    checked_out_at TIMESTAMP,
    status VARCHAR(50) NOT NULL, -- WAITING, CALLED, COMPLETED
    updated_by_event_version BIGINT NOT NULL,
    PRIMARY KEY (queue_id, patient_id)
);

CREATE INDEX IF NOT EXISTS idx_queue_id ON waiting_patients_view(queue_id);
CREATE INDEX IF NOT EXISTS idx_patient_id ON waiting_patients_view(patient_id);
CREATE INDEX IF NOT EXISTS idx_status_patients ON waiting_patients_view(status);

-- Event Processing Lag View (for monitoring)
CREATE TABLE IF NOT EXISTS event_lag_metrics (
    id BIGSERIAL PRIMARY KEY,
    metric_timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    event_name VARCHAR(255) NOT NULL,
    events_created_count INT NOT NULL DEFAULT 0,
    events_published_count INT NOT NULL DEFAULT 0,
    events_processed_count INT NOT NULL DEFAULT 0,
    average_lag_ms DECIMAL(10,2),
    max_lag_ms INT,
    min_lag_ms INT,
    p95_lag_ms INT,
    p99_lag_ms INT
);

CREATE INDEX IF NOT EXISTS idx_metric_timestamp ON event_lag_metrics(metric_timestamp);
CREATE INDEX IF NOT EXISTS idx_event_name_metrics ON event_lag_metrics(event_name);

-- =============================================================================
-- GRANT PERMISSIONS
-- =============================================================================

GRANT ALL PRIVILEGES ON DATABASE rlapp_waitingroom TO rlapp;
GRANT ALL PRIVILEGES ON DATABASE rlapp_waitingroom_read TO rlapp;
GRANT ALL PRIVILEGES ON DATABASE rlapp_waitingroom_test TO rlapp;

\c rlapp_waitingroom
GRANT ALL PRIVILEGES ON SCHEMA public TO rlapp;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rlapp;

\c rlapp_waitingroom_test
GRANT ALL PRIVILEGES ON SCHEMA public TO rlapp;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rlapp;

\c rlapp_waitingroom_read
GRANT ALL PRIVILEGES ON SCHEMA public TO rlapp;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rlapp;
