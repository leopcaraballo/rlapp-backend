# RLAPP Event-Driven Architecture - Visual Reference

## Complete System Architecture

```
┌──────────────────────────────────────────────────────────────────────────┐
│                          CLIENT APPLICATIONS                             │
│              (Web, Mobile, Internal Services)                            │
└──────────────────────────┬───────────────────────────────────────────────┘
                           │ HTTP REST
                           ▼
        ┌──────────────────────────────────┐
        │     WaitingRoom.API (5000)       │
        │  ┌──────────────────────────────┤
        │  │ • POST /api/waiting-room/... │
        │  │ • GET /health                │
        │  │ • CRUD endpoints             │
        │  └──────────────────────────────┤
        │  Middleware:                     │
        │  • CorrelationId tracking        │
        │  • ExceptionHandling             │
        │  • HealthChecks                  │
        └──────────┬──────────────────────┘
                   │ Commands
                   ▼
        ┌──────────────────────────────────┐
        │   Application Layer              │
        │  ┌──────────────────────────────┤
        │  │ CheckInPatientCommandHandler  │
        │  │ • Validates command           │
        │  │ • Loads aggregate             │
        │  │ • Executes behavior           │
        │  │ • Publishes events            │
        │  └──────────────────────────────┤
        └──────────┬──────────────────────┘
                   │
        ┌──────────┴──────────┐
        │                     │
        ▼                     ▼
    Domain Layer      Event Lag Tracking
    ┌──────────────┐   ┌──────────────────┐
    │WaitingQueue  │   │ IEventLagTracker │
    │  Aggregate   │   │                  │
    │              │   │ Records:         │
    │ • CheckIn... │   │ • CREATED        │
    │ • Call...    │   │ • PUBLISHED      │
    │ • Remove...  │   │ • PROCESSED      │
    │              │   │ • FAILED         │
    │ Invariants:  │   └──────────────────┘
    │ • Capacity   │
    │ • Uniqueness │
    │ • Priority   │
    └──────────────┘
        │ Uncommitted Events
        ▼
    ┌──────────────────────────────┐
    │  PostgreSQL EventStore       │
    │  (waiting_room_eventstore)   │
    │                              │
    │ Immutable Event Log:         │
    │ CREATE TABLE                 │
    │  waiting_room_events (       │
    │   version BIGSERIAL,         │
    │   aggregate_id UUID,         │
    │   event_name VARCHAR,        │
    │   payload JSONB,             │
    │   metadata JSONB             │
    │  );                          │
    │                              │
    │ Tracking Table:              │
    │ CREATE TABLE                 │
    │  event_processing_lag (      │
    │   event_name VARCHAR,        │
    │   aggregate_id UUID,         │
    │   event_created_at TIMESTAMP,│
    │   event_published_at TS,     │
    │   projection_processed_at TS,│
    │   total_lag_ms INT,          │
    │   status VARCHAR             │
    │  );                          │
    └──────────────────────────────┘
        │
        ├─[CREATED]─────────────────────┬──→ Lag Tracking Starts
        │                               │
        ▼                               ▼
    ┌──────────────────────────────┐ PostgreSQL
    │ Transactional Outbox         │ event_processing_lag
    │ waiting_room_outbox          │     ↓
    │                              │ Status: CREATED
    │ • published = false          │ timestamp: OccurredAt
    │ • retry_count = 0            │
    │ • attempted_at = null        │
    └──────────────────────────────┘
        │
        ▼ Polling Interval (default: 5s)
    ┌──────────────────────────────┐
    │ WaitingRoom.Worker           │
    │ (OutboxWorker Service)       │
    │                              │
    │ Loop:                        │
    │ 1. SELECT WHERE published=0  │
    │ 2. Batch (max 100)           │
    │ 3. Publish to RabbitMQ       │
    │ 4. Exponential retry         │
    │ 5. Mark published=true       │
    └──────────────────────────────┘
        │
        ├─[PUBLISHED]───────────────────┬──→ Lag Tracking
        │ DispatchDurationMs recorded   │
        │                               ▼
        │                           PostgreSQL
        │                           event_processing_lag
        │                                ↓
        │                           Status: PUBLISHED
        │                           event_published_at
        │                           outbox_dispatch_duration_ms
        │
        ▼ JSON Message
    ┌──────────────────────────────┐
    │    RabbitMQ Broker           │
    │                              │
    │ Exchange:                    │
    │  waiting_room_events(topic)  │
    │                              │
    │ Queue:                       │
    │  waiting-room-projection-q   │
    │                              │
    │ Binding:                     │
    │  waiting.room.* →→→→→→→→→→→  │
    └──────────────────────────────┘
        │
        ▼ BasicConsumer (Manual Ack)
    ┌──────────────────────────────┐
    │ WaitingRoom.Projections      │
    │ (ProjectionWorker Service)   │
    │                              │
    │ ┌────────────────────────────┤
    │ │ RabbitMqProjectionSubsc...  │
    │ │ • Subscribes to topic       │
    │ │ • Deserializes JSON         │
    │ │ • Error handling            │
    │ └────────────────────────────┤
    │           │                  │
    │           ▼                  │
    │ ┌────────────────────────────┤
    │ │ ProjectionEventProcessor    │
    │ │ • Routes to handler         │
    │ │ • Executes idempotently    │
    │ │ • Tracks processing lag     │
    │ └────────────────────────────┤
    │           │                  │
    │           ▼                  │
    │ ┌────────────────────────────┤
    │ │ IProjectionHandler          │
    │ │ • PatientCheckedInHandler   │
    │ │ • Idempotency keys          │
    │ │ • Updates read models       │
    │ └────────────────────────────┤
    └──────────────────────────────┘
        │
        ├─[PROCESSED]───────────────────┬──→ Lag Tracking
        │ ProjectingProcessingDuration  │
        │                               ▼
        │                           PostgreSQL
        │                           event_processing_lag
        │                                ↓
        │                           Status: PROCESSED
        │                           projection_processed_at
        │                           projection_processing_duration_ms
        │                           total_lag_ms = ✓ CALCULATION
        │
        ▼ Read Model Updates
    ┌──────────────────────────────┐
    │ PostgreSQL Read Models       │
    │ (waitingroom_read_models)    │
    │                              │
    │ waiting_queue_view:          │
    │  • queue_id (PK)             │
    │  • queue_name                │
    │  • current_patient_count     │
    │  • updated_by_event_version  │
    │                              │
    │ waiting_patients_view:       │
    │  • queue_id (FK)             │
    │  • patient_id (FK)           │
    │  • patient_name              │
    │  • position_in_queue         │
    │  • status                    │
    │  • checked_in_at             │
    │  • updated_by_event_version  │
    │                              │
    │ event_lag_metrics:           │
    │  (Aggregated statistics)     │
    │  • P50, P95, P99 lag         │
    │  • avg throughput            │
    │  • failure count             │
    └──────────────────────────────┘
        │
        ▼ Queryable via API
    API Endpoints:
    GET /api/waiting-room/queue/{queueId}
    GET /api/metrics/lag
    GET /api/projections/health


═══════════════════════════════════════════════════════════════════════════
                                 OBSERVABILITY LAYER
═══════════════════════════════════════════════════════════════════════════

┌─────────────────────────────────────────────────────────────────────────┐
│                         Observability Stack                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  Layer 1: Structured Logging (Seq)                                    │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │ Serilog → Seq (Port 5341)                                  │ │
│  │ • "Event {EventId} processed"                             │ │
│  │ • "OutboxDuration: {Ms}ms"                                │ │
│  │ • Searchable: ProcessingDurationMs > 500                  │ │
│  │ • Full context: CorrelationId tracing                     │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  Layer 2: Metrics Collection (Prometheus)                             │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │ Prometheus (Port 9090)                                      │ │
│  │ • event_processing_lag_ms {event_name}                    │ │
│  │ • events_dispatched_total                                 │ │
│  │ • events_processed_total                                  │ │
│  │ • outbox_pending_count                                    │ │
│  │ • Scrape interval: 15s                                    │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  Layer 3: Visualization (Grafana)                                     │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │ Grafana (Port 3000)                                        │ │
│  │                                                            │ │
│  │ Dashboard 1: Event Processing & Lag Monitoring           │ │
│  │ • Event Processing Lag (ms) — Line Chart               │ │
│  │ • Pending Events Gauge — Gauge Panel                  │ │
│  │ • Event Throughput — Stacked Bar                      │ │
│  │ • Processing Failures — Line Chart                    │ │
│  │                                                            │ │
│  │ Dashboard 2: Infrastructure Monitoring                 │ │
│  │ • PostgreSQL Connection Pool — Gauge                 │ │
│  │ • RabbitMQ Queue Depth — Gauge                       │ │
│  │ • Database Size — Line Chart                         │ │
│  │ • Container Memory — Time Series                     │ │
│  │                                                            │ │
│  │ Alerts (Prometheus Alert Rules):                      │ │
│  │ • HighEventProcessingLag (> 1s)                    │ │
│  │ • OutboxWorkerBehind (> 100 pending)               │ │
│  │ • ProjectionCheckpointStale (> 5m)                 │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  Layer 4: Long-term Storage (PostgreSQL)                             │
│  ┌───────────────────────────────────────────────────────────────┐ │
│  │ event_processing_lag table                                 │ │
│  │ • 30-day retention for analysis                           │ │
│  │ • Drift detection                                         │ │
│  │ • Performance trending                                    │ │
│  │ • Query: Percentile calculations                        │ │
│  └───────────────────────────────────────────────────────────────┘ │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘


═══════════════════════════════════════════════════════════════════════════
                              EVENT LAG TIMELINE
═══════════════════════════════════════════════════════════════════════════

T=0ms         │ Event Created
              │ PatientCheckedIn emitted
              │ Status = CREATED
              │ ↓ event_processing_lag
              │
              ├─ record_created()
              │
T=2-3ms       │ ↓ Persisted to EventStore
              │ ↓ Persisted to Outbox (same transaction)
              │
T=0-5000ms    │ ↓ Pending in Outbox
              │ published = false
              │
T=2500ms      │ OutboxWorker wakes up (polling interval)
(±offset)     │
              ├─ Dispatches batch (max 100 messages)
              │ ├─ Publishes to RabbitMQ
              │ ├─ Mark published=true
              │ └─ record_published()
              │    OutboxDispatchDurationMs = 50ms
              │ Status = PUBLISHED
              │
T=2550ms      │ ↓ Message in RabbitMQ
              │ Durable queue, manual ack
              │
T=2550-2650ms │ ProjectionWorker receives message
              │ ├─ Deserialize
              │ ├─ Find handler (PatientCheckedInHandler)
              │ ├─ Execute handler (idempotently)
              │ │  ├─ Update waiting_queue_view
              │ │  ├─ Update waiting_patients_view
              │ │  └─ Mark processed
              │ ├─ Acknowledge to broker
              │ └─ record_processed()
              │    ProjectionProcessingDurationMs = 100ms
              │ Status = PROCESSED
              │
T=2650ms      │ ↓ Lag Calculation
              │ TotalLagMs = 2650 - 0 = 2650ms
              │
              │ ✓ Available in Grafana dashboard
              │ ✓ Queryable via API
              │ ✓ Searchable in Seq logs
              │ ✓ Alarmable in Prometheus


═══════════════════════════════════════════════════════════════════════════
                        SERVICE DEPENDENCIES
═══════════════════════════════════════════════════════════════════════════

WaitingRoom.API
├── WaitingRoom.Application
│   ├── WaitingRoom.Domain
│   │   └── BuildingBlocks.EventSourcing
│   └── WaitingRoom.Infrastructure
│       ├── PostgreSQL (EventStore, Outbox)
│       ├── RabbitMQ (MessageBroker)
│       └── Observability (LagTracking)
│
WaitingRoom.Worker (OutboxWorker)
├── WaitingRoom.Infrastructure
│   ├── PostgreSQL (Outbox polling)
│   ├── RabbitMQ (Publishing)
│   └── Observability (Metrics)
└── BuildingBlocks.EventSourcing
│
WaitingRoom.Projections
├── WaitingRoom.Infrastructure
│   ├── RabbitMQ (EventSubscriber)
│   └── PostgreSQL (ReadModels)
├── WaitingRoom.Domain (EventHandlers)
└── Observability (LagTracking)


═══════════════════════════════════════════════════════════════════════════
                            FAILURE SCENARIOS
═══════════════════════════════════════════════════════════════════════════

Scenario 1: OutboxWorker Crashes
┌─────────────────────────────────────────────────────────┐
│ Events accumulate in outbox                             │
│ published = false                                       │
│ Grafana Alert: OutboxWorkerBehind (pending > 100)     │
│                                                         │
│ Recovery:                                               │
│ 1. Restart OutboxWorker                                │
│ 2. Exponential backoff kicks in                        │
│ 3. All pending messages flush to RabbitMQ             │
│ 4. Projections catch up                                │
│ 5. Lag metrics normalize                               │
└─────────────────────────────────────────────────────────┘

Scenario 2: RabbitMQ Unavailable
┌─────────────────────────────────────────────────────────┐
│ OutboxWorker can't publish                              │
│ Message stays in outbox (published = false)             │
│ Retry logic: Exp backoff (30s → 1h)                    │
│                                                         │
│ Recovery:                                               │
│ 1. RabbitMQ comes back online                          │
│ 2. OutboxWorker resumes polling                        │
│ 3. Queued messages flush automatically                 │
│ 4. Projections process backlog                         │
└─────────────────────────────────────────────────────────┘

Scenario 3: Projection Handler Fails
┌─────────────────────────────────────────────────────────┐
│ Event arrives at ProjectionWorker                       │
│ Handler throws exception                                │
│ Message NACKed (requeue=true)                           │
│ Stays in RabbitMQ queue for retry                       │
│                                                         │
│ Recovery:                                               │
│ 1. Retry consumer (automatic)                          │
│ 2. If persistent: Manual trigger rebuild               │
│ 3. POST /api/projections/rebuild                       │
│ 4. Clear bad state, replay from EventStore             │
│ 5. Read models rehydrated                              │
└─────────────────────────────────────────────────────────┘

