# RLAPP — Infrastructure & Implementation Details

**Descripción técnica de la infraestructura, implementaciones concretas y decisiones tecnológicas.**

---

## 🏗️ Capas de Infraestructura

### Diagrama de Capas

```
┌─────────────────────────────────────────────────────────┐
│               EXTERNAL SYSTEMS                           │
├─────────────────────────────────────────────────────────┤
│  ┌───────────────┐  ┌────────────────┐  ┌─────────────┐ │
│  │  PostgreSQL   │  │   RabbitMQ     │  │  Prometheus │ │
│  │  16-Alpine    │  │  3.12          │  │  + Grafana  │ │
│  └───────────────┘  └────────────────┘  └─────────────┘ │
└────────┬─────────────────┬──────────────────┬────────────┘
         │                 │                  │
         ▼                 ▼                  ▼
┌─────────────────┐ ┌────────────────┐ ┌──────────────┐
│  Event Store    │ │  Message Broker│ │   Metrics    │
│  + Outbox       │ │  + Dispatch    │ │   + Alerts   │
└────────┬────────┘ └────────┬───────┘ └──────────────┘
         │                   │
         └───────┬───────────┘
                 │
    ┌────────────▼─────────────┐
    │  INFRASTRUCTURE LAYER     │
    ├──────────────────────────┤
    │  Persistence/            │
    │  ├─ PostgresEventStore    │
    │  ├─ PostgresOutboxStore   │
    │  ├─ EventStoreSchema      │
    │  │                        │
    │  Messaging/              │
    │  ├─ RabbitMqEventPub     │
    │  ├─ OutboxEventPub       │
    │  ├─ RabbitMqOptions      │
    │  │                        │
    │  Serialization/          │
    │  ├─ EventSerializer      │
    │  ├─ EventTypeRegistry    │
    │  │                        │
    │  Observability/          │
    │  ├─ PostgresEventLagTracker
    │  │                        │
    │  Utility/                │
    │  └─ SystemClock          │
    └──────────────────────────┘
```

---

## 💾 Persistencia (Event Store)

### PostgreSQL Event Store

**Archivo:** `WaitingRoom.Infrastructure/Persistence/EventStore/PostgresEventStore.cs`

#### Responsabilidades

1. **Cargar agregados** desde historic de eventos
2. **Guardar eventos** de forma atómica
3. **Consultar todos los eventos** en orden deterministas
4. **Crear esquema** automáticamente

#### Esquema de Base de Datos

**Tabla: `waiting_room_events`**

```sql
CREATE TABLE waiting_room_events (
    id BIGSERIAL PRIMARY KEY,
    event_id UUID NOT NULL UNIQUE,
    aggregate_id VARCHAR(255) NOT NULL,
    version BIGINT NOT NULL,
    event_name VARCHAR(255) NOT NULL,
    occurred_at TIMESTAMP NOT NULL,
    correlation_id UUID NOT NULL,
    causation_id UUID NOT NULL,
    actor VARCHAR(255) NOT NULL,
    idempotency_key UUID NOT NULL UNIQUE,
    schema_version INT NOT NULL DEFAULT 1,
    payload JSONB NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),

    -- Constraints
    CONSTRAINT unique_aggregate_version
        UNIQUE (aggregate_id, version),

    -- Indexes (performance)
    INDEX idx_aggregate_id (aggregate_id),
    INDEX idx_event_name (event_name),
    INDEX idx_occurred_at (occurred_at),
    INDEX idx_idempotency_key (idempotency_key)
);
```

#### Operaciones Clave

##### LoadAsync() - Reconstruir Agregado

```csharp
public async Task<WaitingQueue?> LoadAsync(
    string aggregateId,
    CancellationToken cancellationToken = default)
{
    var events = await GetEventsAsync(aggregateId, cancellationToken);

    if (events.Count == 0)
        return null;

    // Reflection-based event replay
    return AggregateRoot.LoadFromHistory<WaitingQueue>(
        aggregateId, events);
}
```

**SQL generado:**

```sql
SELECT event_name AS EventName, payload AS Payload
FROM waiting_room_events
WHERE aggregate_id = @AggregateId
ORDER BY version;
```

**Complejidad:** O(n) donde n = # eventos para agregado

**Optimización futura:** Snapshot pattern cada 100 eventos

##### SaveAsync() - Guardar Eventos Atomicamente

```csharp
public async Task SaveAsync(
    WaitingQueue aggregate,
    CancellationToken cancellationToken = default)
{
    // PASO 1: Validar versión (detectar concurrencia)
    var currentVersion = await GetCurrentVersionAsync(...);
    var expectedVersion = aggregate.Version - uncommitted.Count;

    if (currentVersion != expectedVersion)
        throw new EventConflictException(...);

    // PASO 2: Transacción atómica
    using var transaction = await connection.BeginTransactionAsync();

    // PASO 3: Insertar eventos
    foreach (var @event in uncommitted)
    {
        await connection.ExecuteAsync(
            "INSERT INTO waiting_room_events (...) VALUES (?)",
            parameters,
            transaction);
    }

    // PASO 4: Insertar mensajes de Outbox en MISMA TX
    await _outboxStore.AddAsync(messages, connection, transaction);

    // PASO 5: Commit (todo o nada)
    await transaction.CommitAsync();
}
```

**Garantías:**

- **Atomicidad:** O todos los eventos se guardan o ninguno
- **Idempotencia:** Mismo evento 2x → solo se guarda 1x (unique idempotency_key)
- **Orden determinístico:** `version` garantiza secuencia
- **Detección de conflictos:** Si 2 comandos modifican mismo agregado concurrentemente

#### GetAllEventsAsync() - Proyecciones

```csharp
public async Task<IEnumerable<DomainEvent>> GetAllEventsAsync(
    CancellationToken cancellationToken = default)
{
    const string sql = @"
        SELECT event_name AS EventName, payload AS Payload
        FROM waiting_room_events
        ORDER BY version;";

    var rows = await connection.QueryAsync<EventRow>(command);
    return rows.Select(row => _serializer.Deserialize(
        row.EventName, row.Payload)).ToList();
}
```

**Uso:** Rebuild completo de proyecciones sin parar el sistema.

---

## 📬 Outbox Pattern

### Tabla: `waiting_room_outbox`

```sql
CREATE TABLE waiting_room_outbox (
    id BIGSERIAL PRIMARY KEY,
    outbox_id UUID NOT NULL UNIQUE,
    event_id UUID NOT NULL UNIQUE,
    event_name VARCHAR(255) NOT NULL,
    occurred_at TIMESTAMP NOT NULL,
    correlation_id UUID NOT NULL,
    causation_id UUID NOT NULL,
    payload JSONB NOT NULL,
    status VARCHAR(50) NOT NULL,  -- Pending, Dispatched, Failed
    attempts INT NOT NULL DEFAULT 0,
    next_attempt_at TIMESTAMP,    -- NULL = ready now
    last_error TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP NOT NULL DEFAULT NOW(),

    -- Indexes
    INDEX idx_status (status),
    INDEX idx_next_attempt_at (next_attempt_at),
    INDEX idx_event_id (event_id)
);
```

#### PostgresOutboxStore

```csharp
public sealed class PostgresOutboxStore : IOutboxStore
{
    // GetPendingAsync: Fetch messages ready for dispatch
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT * FROM waiting_room_outbox
            WHERE status = @Status
              AND (next_attempt_at IS NULL OR next_attempt_at <= NOW())
            ORDER BY occurred_at
            LIMIT @BatchSize;";

        // Returns messages ordered by creation time (FIFO)
    }

    // MarkDispatchedAsync: Update status after successful publish
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
            WHERE event_id = ANY(@EventIds);";
    }

    // MarkFailedAsync: Retry with backoff
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
            WHERE event_id = ANY(@EventIds);";
    }
}
```

#### Retry Logic con Exponential Backoff

```
Intento 1: Failed → wait 30s
         ↓
Intento 2: Failed → wait 60s
         ↓
Intento 3: Failed → wait 120s
         ↓
Intento 4: Failed → wait 240s (máx 1 hora)
         ↓
Intento 5: Failed → POISON MESSAGE (log + manual intervention)
```

---

## 📨 Messaging

### RabbitMQ Event Publisher

**Archivo:** `WaitingRoom.Infrastructure/Messaging/RabbitMqEventPublisher.cs`

#### Configuración

```csharp
public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "waiting_room_events";
    public string ExchangeType { get; set; } = "topic";
}
```

#### PublishAsync()

```csharp
public async Task PublishAsync(
    IEnumerable<DomainEvent> events,
    CancellationToken cancellationToken = default)
{
    var factory = new ConnectionFactory
    {
        HostName = _options.HostName,
        Port = _options.Port,
        UserName = _options.UserName,
        Password = _options.Password,
        VirtualHost = _options.VirtualHost
    };

    using var connection = factory.CreateConnection();
    using var channel = connection.CreateModel();

    // 1. Declare exchange (idempotent)
    channel.ExchangeDeclare(
        exchange: _options.ExchangeName,
        type: _options.ExchangeType,  // "topic"
        durable: true,
        autoDelete: false);

    // 2. Publish each event
    foreach (var @event in events)
    {
        var payload = _serializer.Serialize(@event);
        var body = Encoding.UTF8.GetBytes(payload);

        var properties = channel.CreateBasicProperties();
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2;  // Persistent
        properties.CorrelationId = @event.Metadata.CorrelationId;
        properties.MessageId = @event.Metadata.IdempotencyKey;

        channel.BasicPublish(
            exchange: _options.ExchangeName,
            routingKey: @event.EventName,  // "PatientCheckedIn"
            mandatory: false,
            basicProperties: properties,
            body: body);

        _logger.LogInformation(
            "Published event {EventName} to RabbitMQ",
            @event.EventName);
    }
}
```

#### Topic Exchange Pattern

```
Exchange: waiting_room_events (topic)
          ├─ Binding: PatientCheckedIn
          │  └─ Queue: projections.waiting_room
          │     ↓ Subscribers: Projection handlers
          │
          └─ Binding: WaitingQueueCreated
             └─ Queue: projections.waiting_room
```

---

## 🔄 Serialización

### EventSerializer

**Archivo:** `WaitingRoom.Infrastructure/Serialization/EventSerializer.cs`

```csharp
public sealed class EventSerializer : IEventSerializer
{
    private readonly EventTypeRegistry _registry;
    private readonly JsonSerializerSettings _settings;

    public EventSerializer(EventTypeRegistry registry)
    {
        _settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    // Serialize: Event → JSON
    public string Serialize(DomainEvent @event)
    {
        return JsonConvert.SerializeObject(@event, _settings);
    }

    // Deserialize: JSON → Event
    public DomainEvent Deserialize(string eventName, string payload)
    {
        var eventType = _registry.GetType(eventName);
        var deserialized = JsonConvert.DeserializeObject(
            payload, eventType, _settings);

        if (deserialized is not DomainEvent domainEvent)
            throw new InvalidOperationException(...);

        return domainEvent;
    }
}
```

### EventTypeRegistry

```csharp
public sealed class EventTypeRegistry
{
    private readonly Dictionary<string, Type> _byName;

    public static EventTypeRegistry CreateDefault() =>
        new([
            typeof(WaitingQueueCreated),
            typeof(PatientCheckedIn)
        ]);

    public Type GetType(string eventName)
    {
        if (!_byName.TryGetValue(eventName, out var eventType))
            throw new InvalidOperationException(
                $"Unknown event type '{eventName}'");

        return eventType;
    }
}
```

**Ventaja:** Agregar nuevo evento es 1 línea (add to registry).

---

## 📊 Observabilidad

### PostgresEventLagTracker

**Archivo:** `WaitingRoom.Infrastructure/Observability/PostgresEventLagTracker.cs`

#### Tabla: `event_lag_metrics`

```sql
CREATE TABLE event_lag_metrics (
    id BIGSERIAL PRIMARY KEY,
    event_id UUID NOT NULL UNIQUE,
    event_name VARCHAR(255) NOT NULL,
    aggregate_id VARCHAR(255) NOT NULL,
    event_created_at TIMESTAMP NOT NULL,
    event_published_at TIMESTAMP,
    outbox_dispatch_duration_ms INT,
    event_processed_at TIMESTAMP,
    projection_processing_duration_ms INT,
    total_lag_ms INT,
    status VARCHAR(50),  -- CREATED, PUBLISHED, PROCESSED, FAILED
    failure_reason TEXT,
    recorded_at TIMESTAMP NOT NULL DEFAULT NOW()
);
```

#### Trazabilidad de Eventos

```
Event lifecycle:
  CREATED (10:00:00)
    ↓ +5s
  PUBLISHED (10:00:05)
    ↓ +2s
  PROCESSED (10:00:07)

Metrics (en PostgreSQL):
  {
    event_id: "...",
    status: "PROCESSED",
    outbox_dispatch_duration_ms: 5000,
    projection_processing_duration_ms: 2000,
    total_lag_ms: 7000
  }
```

#### Dashboard Grafana

**Queries:**

```sql
-- Average lag by event type
SELECT event_name, AVG(total_lag_ms) as avg_lag
FROM event_lag_metrics
WHERE recorded_at > NOW() - INTERVAL '1 hour'
GROUP BY event_name;

-- Percentiles
SELECT event_name,
       PERCENTILE_CONT(0.50) WITHIN GROUP (ORDER BY total_lag_ms) as p50,
       PERCENTILE_CONT(0.90) WITHIN GROUP (ORDER BY total_lag_ms) as p90,
       PERCENTILE_CONT(0.99) WITHIN GROUP (ORDER BY total_lag_ms) as p99
FROM event_lag_metrics
WHERE recorded_at > NOW() - INTERVAL '1 hour'
GROUP BY event_name;
```

---

## ⏰ Clock Abstraction

**Archivo:** `WaitingRoom.Application/Services/SystemClock.cs`

```csharp
public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

**Beneficio:** Tests pueden inyectar `FakeClock` para control de tiempo.

```csharp
public sealed class FakeClock : IClock
{
    private DateTime _now = DateTime.UtcNow;

    public DateTime UtcNow => _now;

    public void Advance(TimeSpan time) => _now = _now.Add(time);
}
```

---

## 🏥 Docker Compose Stack

**Archivo:** `docker-compose.yml`

```yaml
version: "3.8"

services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_USER: rlapp
      POSTGRES_PASSWORD: rlapp_secure_password
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./infrastructure/postgres/init.sql:/docker-entrypoint-initdb.d/01-init.sql

  rabbitmq:
    image: rabbitmq:3.12-management-alpine
    ports:
      - "5672:5672"      # AMQP
      - "15672:15672"    # Management UI
    volumes:
      - rabbitmq_data:/var/lib/rabbitmq
      - ./infrastructure/rabbitmq/rabbitmq.conf:/etc/rabbitmq/rabbitmq.conf:ro

  prometheus:
    image: prom/prometheus:latest
    ports:
      - "9090:9090"
    volumes:
      - ./infrastructure/prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus_data:/prometheus

  grafana:
    image: grafana/grafana:latest
    ports:
      - "3000:3000"
    volumes:
      - grafana_data:/var/lib/grafana
    environment:
      GF_SECURITY_ADMIN_PASSWORD: admin

volumes:
  postgres_data:
  rabbitmq_data:
  prometheus_data:
  grafana_data:
```

---

## 🔌 Dependency Injection

**Archivo:** `WaitingRoom.API/Program.cs`

```csharp
// Infrastructure - Persistence
services.AddSingleton<PostgresOutboxStore>();
services.AddSingleton<IOutboxStore>(sp => sp.GetRequiredService<PostgresOutboxStore>());
services.AddSingleton<IEventStore>(sp =>
    new PostgresEventStore(connectionString, serializer, outboxStore, lagTracker));

// Infrastructure - Messaging
services.AddSingleton<IEventPublisher, OutboxEventPublisher>();  // API: no-op
// In Worker: new RabbitMqEventPublisher(options, serializer, outboxStore)

// Infrastructure - Serialization
services.AddSingleton<EventTypeRegistry>(sp => EventTypeRegistry.CreateDefault());
services.AddSingleton<EventSerializer>();

// Application - Clock
services.AddSingleton<IClock, SystemClock>();

// Application - Handlers
services.AddScoped<CheckInPatientCommandHandler>();

// API
services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres");
```

---

## 🚀 Escabilidad

### Limitaciones Actuales

| Factor | Límite | Bottleneck |
|--------|--------|-----------|
| **Event Load** | 1000 events/sec | PostgreSQL write capacity |
| **Projection Lag** | Eventual (< 1 min) | OutboxWorker polling interval |
| **API Throughput** | ~1000 req/sec | Connection pool (200 conn) |
| **Message Throughput** | ~100 msg/sec | RabbitMQ single thread |

### Mejoras Sugeridas

1. **Event Store Sharding:** Particionar por aggregateId
2. **Snapshot Pattern:** Cada 100 eventos
3. **CQRS Replication:** Read model en diferentes DB
4. **Message Batching:** Procesar más eventos x polling
5. **Projection Parallelization:** Múltiples workers
6. **Event Stream:** Usar Kafka en lugar de RabbitMQ para alto throughput

---

## 📋 Decisiones Tecnológicas

| Decisión | Justificación | Alternativa |
|----------|--------------|------------|
| **Dapper (no EF)** | Control fino, simplicity | EF (overkill para events) |
| **JSONB en PostgreSQL** | Flexible, queryable, performante | Document DB (eventual consistency) |
| **RabbitMQ (topic)** | Pub/sub, routing flexible | Kafka (overkill inicialmente) |
| **In-Memory Projections** | Tests rápidos, simplicidad | PostgreSQL projections |
| **Reflection para handlers** | Type-safe, extensible | Convention-based |
| **Outbox Table** | Garantía de deliver | Direct publish (risky) |
| **Newtonsoft.Json** | Battle-tested, flexible | System.Text.Json (limited) |

---

**Última actualización:** Febrero 2026
