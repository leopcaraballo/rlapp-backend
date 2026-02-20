# RLAPP — Arquitectura Detallada

**Documento técnico que explica la arquitectura hexagonal, event sourcing y decisiones clave.**

---

## 📐 Modelo Arquitectónico

### Patrón Principal: Hexagonal (Ports & Adapters)

La arquitectura está organizada en **capas concéntricas** independientes:

```
┌─────────────────────────────────────────────────────────────┐
│                  PRESENTATION LAYER                          │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  ASP.NET Core Minimal APIs + Middleware              │    │
│  │  - CorrelationIdMiddleware                           │    │
│  │  - ExceptionHandlerMiddleware                        │    │
│  │  - Endpoints (POST /check-in, GET /monitor, etc.)   │    │
│  │  ✗ NO lógica de negocios                            │    │
│  │  ✓ Mapeo DTO → Command                              │    │
│  └─────────────────────────────────────────────────────┘    │
└────────────────────┬────────────────────────────────────────┘
                     │ COMANDOS
                     ▼
┌─────────────────────────────────────────────────────────────┐
│              APPLICATION LAYER                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  CheckInPatientCommandHandler                        │    │
│  │  - Carga agregado del EventStore                     │    │
│  │  - Delega reglas al Domain                           │    │
│  │  - Persiste eventos                                  │    │
│  │  - Publica a IEventPublisher (Outbox)               │    │
│  │  ✗ NO reglas de negocios aquí                       │    │
│  │  ✓ PURE ORCHESTRATION                               │    │
│  │                                                       │    │
│  │  Excepciones:                                        │    │
│  │  - AggregateNotFoundException                        │    │
│  │  - EventConflictException                            │    │
│  │  - ApplicationException                              │    │
│  └─────────────────────────────────────────────────────┘    │
└────────────────────┬────────────────────────────────────────┘
                     │ EVENTOS
                     ▼
┌─────────────────────────────────────────────────────────────┐
│                    DOMAIN LAYER                              │
│  ┌─────────────────────────────────────────────────────┐    │
│  │                                                       │    │
│  │  AGREGADOS:                                          │    │
│  │  └─ WaitingQueue                                     │    │
│  │     ├─ Propiedades: Id, Version, Patients[]         │    │
│  │     ├─ Métodos:                                      │    │
│  │     │  ├─ Create()      → WaitingQueueCreated       │    │
│  │     │  ├─ CheckInPatient() → PatientCheckedIn       │    │
│  │     │  └─ When() [privado] → apply events           │    │
│  │     └─ Invariantes:                                  │    │
│  │        ├─ MaxCapacity never exceeded                │    │
│  │        ├─ No duplicate patients                     │    │
│  │        └─ Valid priorities only                     │    │
│  │                                                       │    │
│  │  EVENTOS DE DOMINIO:                                 │    │
│  │  ├─ WaitingQueueCreated                              │    │
│  │  └─ PatientCheckedIn                                 │    │
│  │                                                       │    │
│  │  VALUE OBJECTS:                                      │    │
│  │  ├─ WaitingQueueId                                   │    │
│  │  ├─ PatientId                                        │    │
│  │  ├─ Priority (Low, Medium, High, Urgent)            │    │
│  │  └─ ConsultationType (General, Cardiology, etc.)    │    │
│  │                                                       │    │
│  │  ENTIDADES:                                          │    │
│  │  └─ WaitingPatient (dentro del agregado)            │    │
│  │                                                       │    │
│  │  INVARIANTES:                                        │    │
│  │  └─ WaitingQueueInvariants                           │    │
│  │                                                       │    │
│  │  ✓ ZERO external dependencies                        │    │
│  │  ✓ PURE business logic                              │    │
│  │  ✓ TESTABLE sin mock (reflection en AggregateRoot)  │    │
│  │  ✓ DETERMINISTIC (same input → same output)         │    │
│  │                                                       │    │
│  └─────────────────────────────────────────────────────┘    │
└────────────────────┬────────────────────────────────────────┘
                     │
                     │ PERSISTENCIA → EventStore
                     │ QUERIES → IEventPublisher
                     │
┌────────────────────▼────────────────────────────────────────┐
│             INFRASTRUCTURE LAYER                             │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  PERSISTENCE:                                        │    │
│  │  ├─ PostgresEventStore (IEventStore impl.)          │    │
│  │  │  ├─ SaveAsync: Insert events + Outbox (atomic)  │    │
│  │  │  ├─ LoadAsync: Replay events                     │    │
│  │  │  └─ GetAllEventsAsync: Deterministic order       │    │
│  │  │                                                   │    │
│  │  │  Tabla: waiting_room_events (JSONB)             │    │
│  │  │  Tabla: waiting_room_outbox (status tracking)   │    │
│  │  │                                                   │    │
│  │  ├─ PostgresOutboxStore (IOutboxStore impl.)       │    │
│  │  │  ├─ GetPendingAsync: Fetch retry backoff        │    │
│  │  │  ├─ MarkDispatchedAsync: Status update          │    │
│  │  │  └─ MarkFailedAsync: Retry scheduling           │    │
│  │  │                                                   │    │
│  │  MESSAGING:                                         │    │
│  │  ├─ OutboxEventPublisher (IEventPublisher impl.)   │    │
│  │  │  └─ No-op: Outbox worker es el único publisher  │    │
│  │  │                                                   │    │
│  │  ├─ RabbitMqEventPublisher (dispatch to broker)    │    │
│  │  │  └─ PublishAsync → RabbitMQ topics              │    │
│  │  │                                                   │    │
│  │  SERIALIZATION:                                     │    │
│  │  ├─ EventSerializer (JSON → Domain Events)         │    │
│  │  └─ EventTypeRegistry (event type mapping)         │    │
│  │                                                       │    │
│  │  OBSERVABILITY:                                     │    │
│  │  ├─ PostgresEventLagTracker                         │    │
│  │  └─ EventLagMetrics (CREATED/PUBLISHED/PROCESSED)  │    │
│  │                                                       │    │
│  │  UTILITY:                                            │    │
│  │  ├─ SystemClock (IClock impl.)                      │    │
│  │  └─ EventStoreSchema (DDL)                          │    │
│  │                                                       │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                               │
│  EXTERNAL SYSTEMS:                                           │
│  ├─ PostgreSQL (Event Store + Outbox + Lag Metrics)        │
│  ├─ RabbitMQ (Event distribution)                           │
│  ├─ Prometheus (Metrics scraping)                           │
│  └─ Grafana (Dashboards)                                    │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

---

## 🔀 Flujo de Dependencias

### Dirección de Dependencias (Sempre hacia adentro - centro)

```
PRESENTATION ──┐
               │
APPLICATION ──┤─→ DOMAIN
               │
INFRASTRUCTURE─┘
```

**Regla de Oro:** Domain NUNCA depende de nadie.

```
✓ OK:    Application → Domain
✓ OK:    Infrastructure → Domain
✓ OK:    Infrastructure → Application Ports
✓ OK:    Presentation → Application
✗ NEVER: Domain → anything
✗ NEVER: Domain → Infrastructure
```

### Acoplamiento Verificable

| Capa | Dependencias Permitidas | Dependencias Prohibidas |
|------|------------------------|-----------------------|
| Domain | Solo .NET Framework | EF, DB, HTTP, Config |
| Application | Domain + Ports (Interfaces) | Infrastructure |
| Infrastructure | Application Ports + External | Domain business logic |
| Presentation | Application + Exceptions | Infrastructure impls |

---

## 🎯 Patrones Implementados

### 1. Event Sourcing

**Principio:** El estado se reconstruye desde eventos, no se persiste directamente.

```csharp
// Write: Solo eventos se persisten
queue.CheckInPatient(...);  // Genera PatientCheckedIn event
await eventStore.SaveAsync(queue);  // Persiste evento

// Read: Estado se reconstruye
var events = await eventStore.GetEventsAsync(queueId);
var queue = AggregateRoot.LoadFromHistory<WaitingQueue>(queueId, events);
```

**Ventajas:**

- Auditoria completa (todos los cambios son eventos)
- Determinismo (replay → mismo estado)
- Escalabilidad (eventos → cache → queries)

**Invariantes:**

- Eventos son inmutables (record type)
- Versión auto-incrementa
- Idempotency key previene duplicados

### 2. CQRS (Command Query Responsibility Segregation)

**Write Model:**

```
Command → CheckInPatientCommandHandler → Domain → Events → EventStore
                                            ↓
                                        Outbox
```

**Read Model:**

```
Events → ProjectionEventProcessor → ProjectionHandlers → Views
                ↓
       EventLagTracker → Metrics
```

**Separación:** Escribir y leer son completamente independientes.

### 3. Outbox Pattern (Garantía de Entrega)

```
┌──────────────────┐
│  CheckIn Command │
└────────┬─────────┘
         │
         ▼
┌─────────────────────────────┐  ATOMIC
│  EventStore                 │  TRANSACTION
│  + OutboxTable              │
│  (save in single TX)         │
└──────────┬───────────────────┘
           │
           │ (success)
           ▼
┌──────────────────────────────────┐
│  OutboxWorker (BackgroundService) │
│  - Poll every 5 seconds           │
│  - Fetch pending messages         │
│  - Publish to RabbitMQ (idempotent)
│  - Mark as dispatched             │
└──────────────────────────────────┘
           │
           ▼
┌──────────────────────────────┐
│  RabbitMQ                    │
│  (broker keeps until consumed)
└─────────────┬────────────────┘
              │
              ▼
        ┌─────────────┐
        │ Projections │
        └─────────────┘
```

**Garantías:**

- Si TX falla → evento no se persiste
- Si Outbox falla → worker lo reintenta
- Si RabbitMQ falla → backed off retry

### 4. Hexagonal Architecture (Ports & Adapters)

**Puertos (Interfaces):**

```csharp
public interface IEventStore  // Port
{
    Task<WaitingQueue?> LoadAsync(string aggregateId, ...);
    Task SaveAsync(WaitingQueue aggregate, ...);
    Task<IEnumerable<DomainEvent>> GetAllEventsAsync(...);
}

public interface IEventPublisher  // Port
{
    Task PublishAsync(IEnumerable<DomainEvent> events, ...);
}
```

**Adaptadores (Implementaciones):**

```csharp
internal class PostgresEventStore : IEventStore { }
internal class OutboxEventPublisher : IEventPublisher { }
internal class RabbitMqEventPublisher : IEventPublisher { }
```

**Beneficio:** Cambiar de DB o broker sin tocar Domain/Application.

### 5. Repository Pattern (vía Event Sourcing)

```csharp
// CheckInPatientCommandHandler
public async Task<int> HandleAsync(CheckInPatientCommand command, ...)
{
    // Load = Reconstruct from history
    var queue = await _eventStore.LoadAsync(command.QueueId, ...)
        ?? throw new AggregateNotFoundException(...);

    // Execute domain logic
    queue.CheckInPatient(...);  // If invalid → throw DomainException

    // Persist = Save new events (atomically with Outbox)
    await _eventStore.SaveAsync(queue, ...);

    // Publish = Queue for async distribution
    await _eventPublisher.PublishAsync(queue.UncommittedEvents, ...);

    return queue.UncommittedEvents.Count;
}
```

---

## 📊 Capas y Responsabilidades Detalladas

### Domain Layer (WaitingRoom.Domain)

**Responsabilidades:**

- Modelar la realidad del negocio (Wait Room)
- Ejecutar reglas de negocio
- Generar eventos que representan decisiones
- Validar invariantes

**Estructura:**

```
Aggregates/
├─ WaitingQueue (root aggregate)
   └─ Entities/WaitingPatient (only accessible from aggregate)

ValueObjects/
├─ WaitingQueueId
├─ PatientId
├─ Priority
└─ ConsultationType

Events/
├─ WaitingQueueCreated
└─ PatientCheckedIn

Invariants/
└─ WaitingQueueInvariants

Entities/
└─ WaitingPatient

Exceptions/
└─ DomainException
```

**Reglas de Negocio Codificadas:**

- Queue capacity never exceeded
- No duplicate patient check-ins
- Priority must be valid
- Patient name cannot be empty
- Valid consultation types

### Application Layer (WaitingRoom.Application)

**Responsabilidades:**

- Orquestar caso de uso
- Cargar/guardar agregado
- Publicar eventos
- Manejar excepciones de dominio

**Estructura:**

```
Commands/
├─ CheckInPatientCommand

CommandHandlers/
├─ CheckInPatientCommandHandler

DTOs/
├─ CheckInPatientDto
├─ PatientInQueueDto
└─ WaitingQueueDto

Ports/ (interfaces)
├─ IEventStore
└─ IEventPublisher

Services/
└─ SystemClock (IClock impl)

Exceptions/
├─ ApplicationException
├─ AggregateNotFoundException
└─ EventConflictException
```

**Flujo Típico:**

```csharp
1. Recibe Command desde API
2. Carga Agregado: await eventStore.LoadAsync(id)
3. Ejecuta caso de uso: aggregate.DoSomething(...)
   → Si falla → DomainException bubbles
4. Guarda eventos: await eventStore.SaveAsync(aggregate)
   → EventStore + Outbox (transacción atómica)
5. Publica: await eventPublisher.PublishAsync(events)
6. Retorna result
```

### Infrastructure Layer (WaitingRoom.Infrastructure)

**Responsabilidades:**

- Persistir eventos en PostgreSQL
- Gestionar tabla de Outbox
- Publicar a RabbitMQ
- Serializar/deserializar eventos
- Rastrear lag de proyecciones

**Estructura:**

```
Persistence/
├─ EventStore/
│  ├─ PostgresEventStore (IEventStore impl)
│  └─ EventStoreSchema
│
├─ Outbox/
│  ├─ PostgresOutboxStore (IOutboxStore impl)
│  ├─ OutboxMessage
│  └─ IOutboxStore

Messaging/
├─ RabbitMqEventPublisher (IEventPublisher impl)
├─ OutboxEventPublisher (IEventPublisher impl - no-op)
└─ RabbitMqOptions

Serialization/
├─ EventSerializer (IEventSerializer impl)
└─ EventTypeRegistry

Observability/
└─ PostgresEventLagTracker (IEventLagTracker impl)
```

**Decisiones Técnicas:**

- **Dapper** (no EF) → control fino SQL, performance
- **JSONB en PostgreSQL** → flexible schema, queryable
- **Npgsql** → driver nativo, confiable
- **RabbitMQ.Client** → directo, bajo nivel de control

### Presentation Layer (WaitingRoom.API)

**Responsabilidades:**

- Exponar endpoints HTTP
- Mapear DTOs → Commands
- Inyectar CorrelationId
- Manejar excepciones globales
- Proporcionar health checks

**Estructura:**

```
Program.cs
├─ DI Container setup
├─ Middleware pipeline
└─ Endpoint registration

Middleware/
├─ CorrelationIdMiddleware
└─ ExceptionHandlerMiddleware

Endpoints/
└─ WaitingRoomQueryEndpoints

(No "Controllers" - Minimal APIs)
```

---

## 🔄 Flujo Completo de Ejecución

### Caso: Patient Check-In

```
1. CLIENT REQUEST
   POST /api/waiting-room/check-in
   {
     queueId: "QUEUE-01",
     patientId: "PAT-001",
     patientName: "John Doe",
     priority: "High",
     consultationType: "General",
     actor: "nurse-001"
   }

2. PRESENTATION LAYER
   ↓
   CorrelationIdMiddleware
   ├─ Extract CorrelationId from header OR generate new
   ├─ Add to HttpContext.Items
   └─ Add to response headers
   ↓
   Endpoint: POST /api/waiting-room/check-in
   ├─ Map DTO → CheckInPatientCommand
   ├─ Extract correlationId from context
   └─ Call CheckInPatientCommandHandler.HandleAsync(command)

3. APPLICATION LAYER
   ↓
   CheckInPatientCommandHandler.HandleAsync()
   ├─ LoadAsync(queueId)
   │  └─ Aggregate reconstructed from events
   │
   ├─ queue.CheckInPatient() [Domain layer call]
   │  └─ Validates all business rules
   │     └─ If violation → throw DomainException
   │  └─ If valid → raises PatientCheckedIn event
   │     └─ Event added to UncommittedEvents
   │
   ├─ SaveAsync(queue)
   │  ├─ BEGIN TRANSACTION
   │  ├─ INSERT into waiting_room_events (PatientCheckedIn)
   │  ├─ INSERT into waiting_room_outbox (same TX)
   │  ├─ COMMIT TRANSACTION
   │  └─ queue.ClearUncommittedEvents()
   │
   ├─ PublishAsync(events)
   │  └─ OutboxEventPublisher.PublishAsync() [no-op]
   │  └─ Events are already in Outbox
   │
   └─ Return eventCount

4. INFRASTRUCTURE LAYER (Async - Background Worker)
   ↓
   OutboxWorker [BackgroundService]
   ├─ Every 5 seconds
   ├─ Call dispatcher.DispatchBatchAsync()
   │  ├─ GetPendingAsync(batchSize: 100)
   │  │  └─ SELECT * FROM waiting_room_outbox WHERE status = 'Pending'
   │  │
   │  ├─ For each message:
   │  │  ├─ Deserialize to DomainEvent
   │  │  ├─ PublishAsync to RabbitMQ
   │  │  ├─ MarkDispatchedAsync() [UPDATE status = 'Dispatched']
   │  │
   │  └─ If failed → MarkFailedAsync() with retry backoff

5. MESSAGE BROKER (RabbitMQ)
   ↓
   Topic: waiting_room_events.patient_checked_in
   ├─ Message persisted until consumed
   └─ Subscribers: Projections, External systems

6. PROJECTIONS (Async - Event subscribers)
   ↓
   ProjectionEventProcessor
   ├─ Receive PatientCheckedIn from RabbitMQ
   ├─ FindHandler() for PatientCheckedIn
   │  └─ PatientCheckedInProjectionHandler
   │
   ├─ CheckIdempotency() via idempotency key
   │  └─ If already processed → skip
   │
   ├─ HandleAsync()
   │  ├─ UpdateMonitorViewAsync() - increment counter for High priority
   │  ├─ AddPatientToQueueAsync() - add to queue list
   │  └─ MarkProcessedAsync() - mark idempotency key as done
   │
   └─ SaveCheckpointAsync() - track progress (version)

7. RESPONSE TO CLIENT
   ↓
   HTTP 200 OK
   {
     "success": true,
     "message": "Patient checked in successfully",
     "correlationId": "<same as in header>",
     "eventCount": 1
   }
```

---

## ⚡ Características de Desacoplamiento

### 1. Commands vs Events

**Commands (intent):**

- `CheckInPatientCommand` - "Check in a patient"
- NOT persisted
- Can fail (returns exception)
- Synchronous in handler

**Events (fact):**

- `PatientCheckedIn` - "Patient was checked in"
- Persisted immutably
- ALWAYS happened (already persisted)
- Distributed asynchronously

### 2. Write Model vs Read Model

**Write Model (OLTP):**

- `WaitingQueue` aggregate
- Strict consistency
- Validates once per command
- Source of truth

**Read Model (OLAP):**

- `WaitingRoomMonitorView`, `QueueStateView`
- Eventual consistency
- Optimized for queries
- Derived from events

**Nota:** Lectura viene de proyecciones, no de agregado en EventStore.

### 3. Synchronous vs Asynchronous

**Synchronous (Blocking):**

- Command execution (application handler)
- Domain logic validation
- EventStore save

**Asynchronous (Non-Blocking):**

- Outbox dispatch → RabbitMQ
- Projection updates
- Lag tracking

Esto permite que la API responda rápido sin esperar a que todos los proyecciones se actualicen (`eventual consistency`).

---

## 🎬 Estados y Transiciones

### Queue Lifecycle

```
POST /api/reception/register
   -> EnEsperaTaquilla
POST /api/cashier/call-next
   -> EnTaquilla
POST /api/cashier/validate-payment
   -> PagoValidado -> EnEsperaConsulta
POST /api/medical/consulting-room/activate
   -> ConsultingRoomActivated
POST /api/medical/call-next (stationId activo)
   -> LlamadoConsulta
POST /api/medical/start-consultation
   -> EnConsulta
POST /api/medical/finish-consultation
   -> Finalizado

Alternos:
- cashier/mark-payment-pending -> PagoPendiente
- cashier/mark-absent -> AusenteTaquilla -> EnEsperaTaquilla
- cashier/cancel-payment -> CanceladoPorPago
- medical/mark-absent -> AusenteConsulta (1 reintento) o CanceladoPorAusencia
```

---

## 🔐 Invariantes y Validaciones

### Niveles de Validación

```
API Layer:
└─ DTO validation (range, format)

Application Layer:
├─ Command validation (not null)
└─ Aggregate existence check

Domain Layer: ⭐⭐⭐
├─ WaitingQueueInvariants
│  ├─ ValidateCapacity(currentCount, maxCapacity)
│  ├─ ValidateDuplicateCheckIn(patientId, queuedPatientIds)
│  ├─ ValidatePriority(priority)
│  └─ ValidateQueueName(queueName)
│
└─ ValueObject creation
   ├─ PatientId.Create() checks not empty
   ├─ Priority.Create() validates against whitelist
   └─ ConsultationType.Create() validates length
```

**Invariante crítica:** Si Domain.CheckInPatient() no lanza excepción, entonces el evento es válido.

---

## 🛠️ Extensibilidad

### Agregar Nuevo Evento

1. **Domain:** Create new event class in `Domain/Events/`
2. **ValueObjects:** Add supporting value objects if needed
3. **Aggregate:** Add `When(NewEvent)` handler method
4. **Registry:** Add to `EventTypeRegistry.CreateDefault()`
5. **Serializer:** Automatic (reflection-based)
6. **Projection:** Create new handler in `Projections/Handlers/`
7. **Tests:** Add tests for new business rule

### Agregar Nueva Proyección

1. **Define View:** Create new DTO in `Projections/Views/`
2. **Implement Handler:** Create `IProjectionHandler` in `Projections/Handlers/`
3. **Register:** Add to `WaitingRoomProjectionEngine._handlers`
4. **Query Endpoint:** Add to `WaitingRoomQueryEndpoints`
5. **Context Method:** Add to `IWaitingRoomProjectionContext`
6. **Tests:** Add projection tests

---

## 📈 Performance Considerations

### Event Store Lookup

```csharp
// O(N) - Loads ALL events for an aggregate
var events = await eventStore.GetEventsAsync(aggregateId);
var queue = AggregateRoot.LoadFromHistory<WaitingQueue>(id, events);
```

**Optimización para agregados grandes:**

- Implementar Snapshot pattern
- Persistir snapshot cada 100 eventos
- Cargar último snapshot + delta

### Projection Updates

```csharp
// O(1) per event - Direct in-memory updates
await context.UpdateMonitorViewAsync(queueId, priority, "increment");
```

**Escalamiento:**

- Proyecciones actuales: In-Memory (tests)
- Futuro: PostgreSQL con índices
- Muy future: Redis cache

---

## 🔍 Debugging y Observabilidad

### Correlation ID

Cada request tiene un ID único rastreado a través de todos los logs:

```
Request: X-Correlation-Id: f47ac10b-58cc-4372-a567-0e02b2c3d479

Logs:
  CorrelationId: f47ac10b-58cc-4372-a567-0e02b2c3d479 - CheckIn request
  CorrelationId: f47ac10b-58cc-4372-a567-0e02b2c3d479 - EventStore save
  CorrelationId: f47ac10b-58cc-4372-a567-0e02b2c3d479 - Outbox dispatch
  CorrelationId: f47ac10b-58cc-4372-a567-0e02b2c3d479 - Projection update
```

### Event Lag Tracking

```
EventLagMetrics:
├─ EventCreatedAt: 2026-02-19T10:00:00Z
├─ EventPublishedAt: 2026-02-19T10:00:05Z (5s - Outbox dispatch)
├─ EventProcessedAt: 2026-02-19T10:00:07Z (2s - Projection)
└─ TotalLagMs: 7000 (Event creation to projection update)
```

Monitor en Grafana para detectar bottlenecks.

---

## ✅ Resumen de Decisiones Arquitectónicas

| Decisión | Justificación | Alternativas |
|----------|--------------|--------------|
| **Event Sourcing** | Auditoría completa, replay, determinismo | CRUD + Snapshots |
| **CQRS** | Modelo de lectura optimizado, escala | Unified model |
| **Outbox Pattern** | Garantía de entrega sin duplicados | Direct publish (risky) |
| **Hexagonal** | Máxima independencia de infraestructura | Monolítico acoplado |
| **Dapper** (no EF) | Control fino, performance, simplicity | EF (overkill for events) |
| **PostgreSQL JSONB** | Flexible schema, queryable, ACID | Document DB (eventual)  |
| **In-Memory Projections** | Tests rápidos, simplicity | PostgreSQL projections |

---

**Última actualización:** Febrero 2026
