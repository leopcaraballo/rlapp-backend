# RLAPP Event-Driven Backend - Quick Reference

**Last Updated:** 2026-02-19
**Implementation Status:** ✅ Complete & Production-Ready

---

## 🚀 Quick Start (5 minutes)

```bash
# 1. Start infrastructure
docker-compose up -d

# Wait for all services to be healthy
docker-compose ps   # All should be "healthy"

# 2. Build & Run Applications (in separate terminals)

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

## 📊 Dashboard Access

| Service | URL | Credentials | Purpose |
|---------|-----|-------------|---------|
| **Grafana** | <http://localhost:3000> | admin/admin123 | Event Lag Dashboards |
| **Prometheus** | <http://localhost:9090> | — | Metrics Queries |
| **Seq** | <http://localhost:5341> | — | Structured Logs |
| **RabbitMQ** | <http://localhost:15672> | guest/guest | Message Broker |
| **PgAdmin** | <http://localhost:5050> | <admin@rlapp.local>/admin123 | Database Admin |
| **API** | <http://localhost:5000> | — | REST Endpoints |

## 🧪 Test Event Flow

```bash
# Create a patient check-in (triggers event flow)
curl -X POST http://localhost:5000/api/waiting-room/check-in \
  -H "Content-Type: application/json" \
  -d '{
    "queueId": "queue-1",
    "patientId": "patient-1",
    "patientName": "John Doe",
    "priority": "HIGH",
    "consultationType": "Cardiology"
  }'

# Response should return: {"success": true, ...}

# Check lag metrics
curl http://localhost:5000/api/metrics/lag?eventName=PatientCheckedIn

# Verify in Grafana: http://localhost:3000
# Dashboard: "RLAPP - Event Processing & Lag Monitoring"
```

## 📈 Key Metrics

**In Grafana Dashboard:**

1. **Event Processing Lag (ms)**
   - Healthy: 0-100ms
   - Warning: 100ms-1s
   - Critical: > 1s

2. **Pending Events**
   - Should be 0 most of the time
   - If > 100, check OutboxWorker logs

3. **Event Throughput**
   - Monitor for consistency
   - If zero: check RabbitMQ connectivity

## 🔍 Monitoring Queries

```sql
-- PostgreSQL: Event lag statistics
SELECT
    event_name,
    COUNT(*) as events,
    ROUND(AVG(total_lag_ms), 2) as avg_lag_ms,
    MAX(total_lag_ms) as max_lag_ms
FROM event_processing_lag
WHERE created_at > NOW() - INTERVAL '1 hour'
  AND status = 'PROCESSED'
GROUP BY event_name;

-- Top 10 slowest events (for debugging)
SELECT
    event_name,
    aggregate_id,
    total_lag_ms
FROM event_processing_lag
WHERE status = 'PROCESSED'
ORDER BY total_lag_ms DESC
LIMIT 10;

-- Outbox status (should be mostly published=true)
SELECT
    published,
    COUNT(*) as count
FROM waiting_room_outbox
GROUP BY published;
```

```promql
# Prometheus: Current lag
avg(event_processing_lag_ms) by (event_name)

# P95 lag (5 minutes)
histogram_quantile(0.95, event_processing_lag_ms)

# Events per second
rate(events_processed_total[1m])

# Queue depth (should be 0)
outbox_pending_count
```

```
# Seq: Structured Log Search
ProcessingDurationMs > 500
EventType = "PatientCheckedIn" AND Level = "Error"
CorrelationId = "abc-123"  // Trace full request
```

## 🛠️ Troubleshooting

### High Lag (> 1 second)

```bash
# 1. Check OutboxWorker
docker-compose logs -f waitingroom-worker | grep error

# 2. Check RabbitMQ connectivity
docker-compose exec rabbitmq rabbitmq-diagnostics -q ping

# 3. Check database performance
docker-compose exec postgres \
  psql -U postgres -d waitingroom_eventstore \
  -c "SELECT count(*) FROM waiting_room_outbox WHERE published=false;"
```

### No Events in Projections

```bash
# 1. Verify RabbitMQ has messages
docker-compose logs -f rabbitmq | grep "completed"

# 2. Check ProjectionWorker logs
docker-compose logs -f waitingroom-projections

# 3. Trigger projection rebuild
curl -X POST http://localhost:5000/api/projections/rebuild
```

### Database Connection Issues

```bash
# 1. Test PostgreSQL connection
docker-compose exec postgres \
  psql -U postgres -d waitingroom_eventstore \
  -c "SELECT 1;"

# 2. Check table schemas
docker-compose exec postgres \
  psql -U postgres -d waitingroom_eventstore \
  -c "\dt"
```

## 📁 Project Structure

```
rlapp-backend/
├── docker-compose.yml                   ← Start all infrastructure
├── infrastructure/                      ← Docker configs
│   ├── postgres/init.sql               ← Database schema
│   ├── rabbitmq/                       ← Message broker config
│   ├── prometheus/                     ← Metrics collection
│   └── grafana/                        ← Dashboards & provisioning
├── docs/
│   ├── DEPLOYMENT_GUIDE.md             ← Detailed operations guide
│   ├── IMPLEMENTATION_SUMMARY.md        ← What was built
│   └── architecture/decisions/
│       ├── ADR-007.md                  ← Full-stack architecture
│       └── ADR-*.md                    ← Other decisions
└── src/
    ├── BuildingBlocks/
    │   └── BuildingBlocks.EventSourcing/
    └── Services/WaitingRoom/
        ├── WaitingRoom.API/            ← REST endpoints
        ├── WaitingRoom.Application/    ← Business logic
        ├── WaitingRoom.Domain/         ← Pure domain model
        ├── WaitingRoom.Infrastructure/ ← Persistence & messaging
        │   ├── Observability/          ← Lag tracking
        │   ├── Persistence/            ← EventStore, Outbox
        │   └── Messaging/              ← RabbitMQ integration
        ├── WaitingRoom.Projections/    ← CQRS read models
        │   ├── EventSubscription/      ← RabbitMQ subscriber
        │   ├── Processing/             ← Event processor
        │   ├── Worker/                 ← Projection worker service
        │   └── Handlers/               ← Event handlers
        └── WaitingRoom.Worker/         ← Outbox worker service
```

## 🔗 Important Files

| File | Purpose |
|------|---------|
| `docker-compose.yml` | Complete infrastructure definition |
| `EventLagTracker.cs` | Lag tracking interface |
| `PostgresEventLagTracker.cs` | Lag metrics persistence |
| `ProjectionWorker.cs` | Projection service |
| `EventDrivenPipelineE2ETests.cs` | Full pipeline tests |
| `DEPLOYMENT_GUIDE.md` | Operations manual |
| `ADR-007*.md` | Architecture decisions |

## 📝 Key Commands

```bash
# Docker
docker-compose up -d                # Start all
docker-compose ps                   # Status
docker-compose logs -f SERVICE      # View logs
docker-compose down                 # Stop all
docker-compose down -v              # Stop & remove volumes

# .NET
dotnet restore                      # Install deps
dotnet build                        # Compile
dotnet run                          # Run
dotnet test                         # Tests

# Database
psql -h localhost -U postgres -d waitingroom_eventstore  # Connect
\dt                                 # List tables
SELECT * FROM event_processing_lag; # Query lag metrics

# API Testing
curl http://localhost:5000/health   # Health check
curl -X POST http://localhost:5000/api/waiting-room/check-in \
  -H "Content-Type: application/json" -d '{...}'
```

## 📞 Support & Resources

- **DEPLOYMENT_GUIDE.md** — Detailed troubleshooting
- **ADR-007** — Architecture decisions & rationale
- **Event Processing Flow** — Diagram in DEPLOYMENT_GUIDE
- **Grafana Dashboards** — Real-time visualization
- **Seq Logs** — Searchable structured logs

## ✅ Health Check

```bash
# Run these to verify system is healthy
curl http://localhost:5000/health/live      # ✓ Should be healthy
curl http://localhost:5000/health/ready     # ✓ Should be ready
docker-compose ps                           # ✓ All should be up
curl http://localhost:3000/api/health       # ✓ Grafana up
curl http://localhost:9090/-/healthy        # ✓ Prometheus up
```

## 🎯 Next Steps

1. **Explore Dashboards** → Open Grafana, create test events
2. **Read ADR-007** → Understand architecture decisions
3. **Run Integration Tests** → Verify full pipeline
4. **Tune Performance** → Adjust polling intervals based on load
5. **Setup Monitoring** → Create alerts in Grafana

---

**Production Ready** ✅ — Deploy confidence level: High
