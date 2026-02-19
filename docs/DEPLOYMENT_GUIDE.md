# RLAPP - Event-Driven Backend Infrastructure Guide

Complete guide for deploying and monitoring the RLAPP event-driven microservices architecture with Docker.

## 🏗️ Architecture Overview

```
┌─────────────────┐
│   HTTP Clients  │
└────────┬────────┘
         │ REST API
         ▼
┌─────────────────────────────────────────┐
│      WaitingRoom.API (Port 5000)        │◄──┐
│ - HTTP Endpoints                        │   │
│ - Request Validation                    │   │
│ - CorrelationId Tracking                │   │
└──────────┬──────────────────────────────┘   │
           │ Commands                          │
           ▼                                   │
┌─────────────────────────────────────────┐   │ Lag
│   Application Layer                     │   │ Monitoring
│ - CommandHandlers                       │   │
│ - Business Orchestration                │   │
└──────────┬──────────────────────────────┘   │
           │                                   │
           ▼                                   │
┌─────────────────────────────────────────┐   │
│   Domain Layer                          │   │
│ - WaitingQueue Aggregate                │   │
│ - PatientCheckedIn Events               │   │
│ - Business Invariants                   │   │
└──────────┬──────────────────────────────┘   │
           │ Events                            │
           ▼                                   │
   ┌───────────────────┐                      │
   │ PostgreSQL        │◄─────────────────────┘
   │ EventStore        │
   │ + Outbox          │
   │ + LagMetrics      │
   │ + ReadModels      │
   └─────────┬─────────┘
             │
             ▼
   ┌──────────────────┐
   │  OutboxWorker    │
   │ - Polls Outbox   │
   │ - Retry Logic    │
   │ - Observability  │
   └─────────┬────────┘
             │ Publishes Events
             ▼
   ┌──────────────────┐
   │   RabbitMQ       │◄─── Projections
   │ Topic Exchange   │     (Real-time)
   └──────────────────┘
             │
             ▼
   ┌──────────────────┐
   │ Projection Worker│
   │ - Subscribes     │
   │ - Updates Views  │
   │ - Tracks Lag     │
   └──────────────────┘

┌─────────────────────────────────────────┐
│      Observability Stack                │
├─────────────────────────────────────────┤
│ - Prometheus (Metrics)                  │
│ - Grafana (Dashboards & Lag Monitoring) │
│ - Seq (Structured Logs)                 │
│ - PostgreSQL (Metrics Storage)          │
└─────────────────────────────────────────┘
```

## 🚀 Quick Start

### Prerequisites

- Docker & Docker Compose
- .NET 10 SDK (for local development)
- Git

### 1. Start Infrastructure

```bash
# From project root
docker-compose up -d

# Verify all services are healthy
docker-compose ps

# Expected output:
# postgres          ✓ running (port 5432)
# rabbitmq          ✓ running (ports 5672, 15672)
# prometheus        ✓ running (port 9090)
# grafana           ✓ running (port 3000)
# seq               ✓ running (port 5341)
# pgadmin           ✓ running (port 5050)
```

### 2. Build .NET Applications

```bash
# Restore dependencies
dotnet restore

# Build all projects
dotnet build --configuration Release

# Expected output:
# ✓ BuildingBlocks.EventSourcing
# ✓ WaitingRoom.Domain
# ✓ WaitingRoom.Application
# ✓ WaitingRoom.Infrastructure
# ✓ WaitingRoom.Projections
# ✓ WaitingRoom.API
# ✓ WaitingRoom.Worker
```

### 3. Initialize Database Schema

```bash
# Run this once to setup tables
dotnet run --project src/Services/WaitingRoom/WaitingRoom.API -- --init-db
```

### 4. Start Services

```bash
# Terminal 1: API Server
cd src/Services/WaitingRoom/WaitingRoom.API
dotnet run --configuration Debug

# Terminal 2: Outbox Worker
cd src/Services/WaitingRoom/WaitingRoom.Worker
dotnet run --configuration Debug

# Terminal 3: Projection Worker
cd src/Services/WaitingRoom/WaitingRoom.Projections
dotnet run --configuration Debug
```

## 📊 Monitoring & Dashboards

### Grafana (Dashboards & Lag Monitoring)

**URL:** <http://localhost:3000>
**Credentials:** `admin` / `admin123`

#### Available Dashboards

1. **Event Processing & Lag Monitoring** (`event-processing.json`)
   - Event processing lag (ms)
   - Pending events in outbox
   - Event throughput (events/sec)
   - Event processing failures

2. **Infrastructure Monitoring** (`infrastructure.json`)
   - PostgreSQL connection pool usage
   - RabbitMQ queue depth
   - Database size growth
   - Container memory usage

#### Key Metrics to Monitor

| Metric | Healthy | Warning | Critical |
|--------|---------|---------|----------|
| Event Processing Lag | < 100ms | 100ms - 1s | > 1s |
| Pending Outbox Events | 0-10 | 10-100 | > 100 |
| Event Throughput | > 100/sec | 50-100/sec | < 50/sec |
| DB Connection Pool | < 50% | 50-75% | > 75% |

### Prometheus (Metrics Scraping)

**URL:** <http://localhost:9090>

Query event lag metrics:

```promql
# Average lag by event type
avg(event_processing_lag_ms) by (event_name)

# Max lag in last 5 minutes
max_over_time(event_processing_lag_ms[5m])

# Outbox pending count
outbox_pending_count

# Events per second
rate(events_dispatched_total[1m])
```

### Seq (Structured Logging)

**URL:** <http://localhost:5341>

Search and filter logs:

```
// Events with high processing lag
ProcessingDurationMs > 500

// Projection processing errors
EventType = "ProjectionError"

// Outbox dispatch failures
Component = "OutboxWorker" AND Level = "Error"
```

### PostgreSQL Admin (PgAdmin)

**URL:** <http://localhost:5050>
**Credentials:** `admin@rlapp.local` / `admin123`

#### Key Tables to Monitor

```sql
-- Event processing lag
SELECT * FROM event_processing_lag
ORDER BY created_at DESC LIMIT 10;

-- Outbox status
SELECT status, COUNT(*) as count, AVG(retry_count) as avg_retries
FROM waiting_room_outbox
GROUP BY status;

-- Projection checkpoints
SELECT * FROM projection_checkpoints;

-- Lag statistics
SELECT * FROM event_lag_metrics
ORDER BY metric_timestamp DESC LIMIT 10;
```

### RabbitMQ Management UI

**URL:** <http://localhost:15672>
**Credentials:** `guest` / `guest`

- **Exchanges:** View `waiting_room_events` topic exchange
- **Queues:** Monitor queue depth and message rates
- **Connections:** Check active subscribers
- **Channels:** Monitor channel health

## 🔍 Event Processing Flow Deep Dive

### 1. Event Creation (Domain)

```csharp
// WaitingQueue.CheckInPatient() creates event
var @event = new PatientCheckedIn
{
    QueueId = queueId,
    PatientId = patientId,
    Priority = priority,
    // ... other fields
    Metadata = new EventMetadata
    {
        OccurredAt = DateTime.UtcNow,
        EventId = Guid.NewGuid().ToString(),
        AggregateId = queueId,
        Version = nextVersion
    }
};

// Recorded in uncommitted events
AddUncommittedEvent(@event);
```

**Tracking:** Lag timer starts at `EventMetadata.OccurredAt`

### 2. Event Persistence (EventStore)

```csharp
// PostgresEventStore.SaveAsync()
INSERT INTO waiting_room_events
(aggregate_id, event_name, payload, metadata, created_at)
VALUES (@QueueId, 'PatientCheckedIn', @Json, @Metadata, now());

// Within same transaction, add to outbox
INSERT INTO waiting_room_outbox
(aggregate_id, event_name, payload, metadata, published)
VALUES (@QueueId, 'PatientCheckedIn', @Json, @Metadata, false);

// Record lag metrics
INSERT INTO event_processing_lag
(event_name, aggregate_id, event_created_at, status)
VALUES ('PatientCheckedIn', @QueueId, @OccurredAt, 'CREATED');
```

**Duration:** Typically < 5ms (single database transaction)

### 3. Outbox Dispatch (OutboxWorker)

```csharp
// OutboxDispatcher.DispatchBatchAsync()
1. SELECT WHERE published = false LIMIT 100
2. For each message:
   - Publish to RabbitMQ (idempotent client ID)
   - On success: UPDATE outbox SET published = true
   - On failure: UPDATE retry_count, last_failed_at
   - Record: UPDATE event_processing_lag SET
     event_published_at = now()

// Retry logic (exponential backoff)
Attempt 1: 30s delay
Attempt 2: 2m delay
Attempt 3: 30m delay
Attempt 4: 1h delay
Attempt 5: Manual intervention required
```

**Duration:** 30-50ms per message (including retries)

**Lag Point:** Event sits in outbox ≈ PollingInterval/2 (default 2.5s)

### 4. Event Subscription (ProjectionWorker)

```csharp
// RabbitMqProjectionEventSubscriber.OnMessageReceived()
1. Receive from RabbitMQ
2. Deserialize JSON event
3. Find matching IProjectionHandler
4. Execute handler (idempotent operation)
5. Update read models
6. Acknowledge message to broker
7. Record: UPDATE event_processing_lag SET
   projection_processed_at = now()
```

**Duration:** 50-200ms (depends on read model complexity)

### 5. Lag Metrics

```sql
-- Total lag calculation
Total Lag = OutboxDispatchDurationMs + ProjectionProcessingDurationMs

-- Example:
-- Event created: 10:00:00.000
-- Published to RabbitMQ: 10:00:02.050 (2050ms)
-- Processed by projection: 10:00:02.200 (150ms)
-- Total lag: 2200ms

SELECT
    event_name,
    AVG(total_lag_ms) as avg_lag,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY total_lag_ms) as p95_lag,
    MAX(total_lag_ms) as max_lag
FROM event_processing_lag
WHERE status = 'PROCESSED'
  AND created_at > now() - interval '1 hour'
GROUP BY event_name;
```

## 🛠️ Troubleshooting

### Problem: High Event Processing Lag (> 1s)

**Check:**

1. **Outbox Worker Stuck?**

   ```bash
   docker-compose logs -f waitingroom-worker
   # Look for errors in outbox dispatch
   ```

2. **RabbitMQ Connectivity?**

   ```bash
   docker-compose exec rabbitmq rabbitmq-diagnostics -q ping
   ```

3. **Projection Handler Bottleneck?**

   ```sql
   SELECT event_name, AVG(projection_processing_duration_ms)
   FROM event_processing_lag
   WHERE status = 'PROCESSED'
   GROUP BY event_name;
   ```

**Solutions:**

- Increase `PollingIntervalSeconds` in OutboxDispatcher config
- Optimize projection handler queries
- Scale Projection Worker instances
- Check PostgreSQL performance: `EXPLAIN ANALYZE`

### Problem: Messages Not Appearing in Projections

**Check:**

1. **Outbox has pending messages?**

   ```sql
   SELECT COUNT(*) FROM waiting_room_outbox WHERE published = false;
   ```

2. **RabbitMQ receiving messages?**
   - Check RabbitMQ Management UI for message rates

3. **Projection Worker running?**

   ```bash
   docker-compose ps projection-worker
   ```

4. **Handler errors in logs?**

   ```bash
   curl http://localhost:5341/logs?query=ProjectionError
   ```

**Solutions:**

- Check `Seq` for structured logs
- Manually trigger rebuild: POST `/api/projections/rebuild`
- Check handler idempotency implementation

### Problem: Database Disk Space Growing Rapidly

**Check:**

1. **Event table size:**

   ```sql
   SELECT pg_size_pretty(pg_total_relation_size('waiting_room_events'));
   ```

2. **Outbox accumulation:**

   ```sql
   SELECT COUNT(*) FROM waiting_room_outbox WHERE published = false;
   ```

3. **Lag metrics table:**

   ```sql
   SELECT count(*) FROM event_processing_lag;
   ```

**Solutions:**

- Archive old events (> 90 days)
- Cleanup lag metrics: `DELETE FROM event_processing_lag WHERE created_at < now() - interval '30 days'`
- Enable PostgreSQL WAL archiving for backup

## 📈 Performance Tuning

### 1. Outbox Worker Tuning

Edit `src/Services/WaitingRoom/WaitingRoom.Worker/appsettings.json`:

```json
{
  "OutboxDispatcher": {
    "PollingIntervalSeconds": 3,      // Faster polling = lower lag
    "BatchSize": 200,                  // More events per batch
    "MaxRetryAttempts": 5,
    "BaseRetryDelaySeconds": 20        // Shorter initial delay
  }
}
```

**Tradeoff:** Lower PollingInterval = higher CPU/DB load

### 2. Projection Processing

Implement efficient handlers:

```csharp
public async Task HandleAsync(DomainEvent @event, IProjectionContext context, CancellationToken cancellation)
{
    // ✓ Good: Batch updates
    await context.BatchUpdateAsync(updates, cancellation);

    // ✗ Avoid: Multiple individual queries
    foreach (var item in items) {
        await context.UpdateAsync(item);
    }
}
```

### 3. RabbitMQ Tuning

Edit `infrastructure/rabbitmq/rabbitmq.conf`:

```conf
# Increase throughput
channel_max = 4096

# Enable persistent queues
rabbit.transient_factor = 0.5

# Tune HA (if running cluster)
ha_sync_batch_size = 10
```

### 4. PostgreSQL Tuning

Edit `docker-compose.yml`:

```yaml
postgres:
  environment:
    POSTGRES_INITDB_ARGS: |
      -c max_connections=500
      -c shared_buffers=256MB
      -c effective_cache_size=1GB
      -c work_mem=16MB
```

## 🚨 Alerting Setup

### Configure Prometheus Alerts

Alerts are defined in `infrastructure/prometheus/alert-rules.yml`:

1. **HighEventProcessingLag** (> 1 min)
   - **Action:** Restart OutboxWorker

2. **OutboxWorkerBehind** (> 1000 pending)
   - **Action:** Check RabbitMQ connectivity

3. **ProjectionCheckpointStale** (> 5 min)
   - **Action:** Trigger projection rebuild

### Setup Alertmanager

```bash
# Create alertmanager config
docker pull prom/alertmanager
docker run -d -p 9093:9093 prom/alertmanager

# Update prometheus.yml to send alerts:
# alerting:
#   alertmanagers:
#     - static_configs:
#         - targets: ['alertmanager:9093']
```

## 📚 API Endpoints

### Health Checks

```bash
# API health
curl http://localhost:5000/health

# Projection status
curl http://localhost:5000/api/projections/health

# Event lag statistics
curl http://localhost:5000/api/metrics/lag?eventName=PatientCheckedIn
```

### Projection Management

```bash
# Trigger rebuild
POST /api/projections/rebuild

# Get projection checkpoint
GET /api/projections/{projectionId}/checkpoint

# Get lag statistics
GET /api/metrics/lag?eventName=PatientCheckedIn&from=2026-02-19&to=2026-02-20
```

## 🧪 Testing the Full Pipeline

```bash
# 1. Create a patient check-in
curl -X POST http://localhost:5000/api/waiting-room/check-in \
  -H "Content-Type: application/json" \
  -d '{
    "queueId": "queue-1",
    "patientId": "patient-1",
    "patientName": "John Doe",
    "priority": "HIGH",
    "consultationType": "Cardiology"
  }'

# 2. Check event in EventStore
curl http://localhost:5000/api/events/queue-1

# 3. Verify outbox dispatch (should be empty if worker is running)
curl http://localhost:5000/api/outbox/pending

# 4. Check read model in projection
curl http://localhost:5000/api/waiting-room/monitor

# 5. View lag metrics
curl http://localhost:5000/api/metrics/lag?eventName=PatientCheckedIn
```

## 📦 Deployment to Production

### 1. Build Docker Images

```bash
# API
docker build -t rlapp-api:latest \
  -f src/Services/WaitingRoom/WaitingRoom.API/Dockerfile .

# Outbox Worker
docker build -t rlapp-worker:latest \
  -f src/Services/WaitingRoom/WaitingRoom.Worker/Dockerfile .

# Projection Worker
docker build -t rlapp-projections:latest \
  -f src/Services/WaitingRoom/WaitingRoom.Projections/Dockerfile .
```

### 2. Docker Stack or Kubernetes

```bash
# Docker Stack
docker stack deploy -c docker-compose.yml rlapp

# Kubernetes
kubectl apply -f k8s/namespace.yml
kubectl apply -f k8s/configmap.yml
kubectl apply -f k8s/secrets.yml
kubectl apply -f k8s/postgres.yml
kubectl apply -f k8s/rabbitmq.yml
kubectl apply -f k8s/api-deployment.yml
kubectl apply -f k8s/worker-deployment.yml
```

### 3. Environment Variables

```bash
# Production configurations
export EventStore__ConnectionString="Host=postgres.prod;Database=rlapp_events;User=rlapp_prod;Password=***"
export RabbitMq__HostName="rabbitmq.prod"
export RabbitMq__Port=5672
export Seq__Url="https://logs.prod.rlapp.com"
```

## 📖 References

- [Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)
- [Transactional Outbox](https://microservices.io/patterns/data/transactional-outbox.html)
- [CQRS](https://martinfowler.com/bliki/CQRS.html)
- [RabbitMQ Tutorials](https://www.rabbitmq.com/getstarted.html)
- [Grafana Dashboards](https://grafana.com/grafana/dashboards/)

---

**Last Updated:** 2026-02-19
**Status:** Production Ready
