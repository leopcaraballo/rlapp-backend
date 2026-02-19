-- =============================================================================
-- RLAPP PostgreSQL Schema Initialization
-- Event Store, Outbox, and Read Models
-- =============================================================================

-- Create databases
CREATE DATABASE waitingroom_eventstore;
CREATE DATABASE waitingroom_read_models;

-- Connect to eventstore database
\c waitingroom_eventstore

-- =============================================================================
-- EVENT STORE SCHEMA
-- =============================================================================

CREATE TABLE IF NOT EXISTS waiting_room_events (
    version BIGSERIAL PRIMARY KEY,
    aggregate_id UUID NOT NULL,
    event_name VARCHAR(255) NOT NULL,
    payload JSONB NOT NULL,
    metadata JSONB NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_aggregate_id ON waiting_room_events(aggregate_id);
CREATE INDEX IF NOT EXISTS idx_event_name ON waiting_room_events(event_name);
CREATE INDEX IF NOT EXISTS idx_created_at ON waiting_room_events(created_at);

-- =============================================================================
-- OUTBOX PATTERN TABLE
-- =============================================================================

CREATE TABLE IF NOT EXISTS waiting_room_outbox (
    id BIGSERIAL PRIMARY KEY,
    aggregate_id UUID NOT NULL,
    event_name VARCHAR(255) NOT NULL,
    payload JSONB NOT NULL,
    metadata JSONB NOT NULL,
    published BOOLEAN NOT NULL DEFAULT FALSE,
    attempted_at TIMESTAMP,
    last_failed_at TIMESTAMP,
    retry_count INT NOT NULL DEFAULT 0,
    exception_message TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    published_at TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_published ON waiting_room_outbox(published);
CREATE INDEX IF NOT EXISTS idx_created_at_outbox ON waiting_room_outbox(created_at);
CREATE INDEX IF NOT EXISTS idx_retry_count ON waiting_room_outbox(retry_count);

-- =============================================================================
-- LAG MONITORING TABLE (Observability)
-- =============================================================================

CREATE TABLE IF NOT EXISTS event_processing_lag (
    id BIGSERIAL PRIMARY KEY,
    event_name VARCHAR(255) NOT NULL,
    aggregate_id UUID NOT NULL,
    event_created_at TIMESTAMP NOT NULL,
    event_published_at TIMESTAMP,
    projection_processed_at TIMESTAMP,
    outbox_dispatch_duration_ms INT,
    projection_processing_duration_ms INT,
    total_lag_ms INT,
    status VARCHAR(50) NOT NULL, -- CREATED, PUBLISHED, PROCESSED
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_event_name_lag ON event_processing_lag(event_name);
CREATE INDEX IF NOT EXISTS idx_status_lag ON event_processing_lag(status);
CREATE INDEX IF NOT EXISTS idx_created_at_lag ON event_processing_lag(created_at);

-- =============================================================================
-- PROJECTION STATE (for rebuild capability)
-- =============================================================================

CREATE TABLE IF NOT EXISTS projection_checkpoints (
    projection_id VARCHAR(255) PRIMARY KEY,
    last_processed_event_version BIGINT NOT NULL DEFAULT 0,
    last_processed_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_healthy BOOLEAN NOT NULL DEFAULT TRUE,
    error_message TEXT,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Connect to read_models database
\c waitingroom_read_models

-- =============================================================================
-- READ MODELS / PROJECTIONS
-- =============================================================================

-- Waiting Queue View (Read Model)
CREATE TABLE IF NOT EXISTS waiting_queue_view (
    queue_id UUID PRIMARY KEY,
    queue_name VARCHAR(255) NOT NULL,
    max_capacity INT NOT NULL,
    current_patient_count INT NOT NULL DEFAULT 0,
    created_at TIMESTAMP NOT NULL,
    last_modified_at TIMESTAMP NOT NULL,
    updated_by_event_version BIGINT NOT NULL
);

-- Waiting Patients View (Read Model)
CREATE TABLE IF NOT EXISTS waiting_patients_view (
    queue_id UUID NOT NULL,
    patient_id UUID NOT NULL,
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

GRANT ALL PRIVILEGES ON DATABASE waitingroom_eventstore TO rlapp;
GRANT ALL PRIVILEGES ON DATABASE waitingroom_read_models TO rlapp;
GRANT ALL PRIVILEGES ON SCHEMA public TO rlapp;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO rlapp;
