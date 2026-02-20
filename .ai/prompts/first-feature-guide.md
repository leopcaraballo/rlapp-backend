FIRST_FEATURE_GUIDE.md

FIRST FEATURE — RLAPP
Paso 1 — Definir Evento

Crear evento en Domain/Events

Reglas:

Nombre en pasado

Hecho del negocio

Inmutable

Versionado

Paso 2 — Crear Aggregate

En Domain/Aggregates

Debe:

Aplicar evento

Emitir evento

Proteger invariantes

Ser determinista

Paso 3 — Crear Unit Tests del Dominio

Validar:

Estados válidos

Estados inválidos

Eventos correctos

Determinismo

Paso 4 — Crear Command

En Application/Commands

Paso 5 — Crear CommandHandler

Debe:

Cargar Aggregate desde eventos

Ejecutar lógica

Persistir nuevos eventos

Paso 6 — Persistir en Event Store

Guardar eventos

Guardar Outbox

Transacción única

Paso 7 — Crear Projection

Consumir evento

Actualizar Read Model

Idempotente

Replayable

Paso 8 — Crear Endpoint API

Sin lógica de negocio

Solo orquestación

Paso 9 — Validar E2E

Check-in → aparece en monitor

Replay → consistente

Evento duplicado → sin corrupción

5. Estructura Exacta de Solución
src/
 ├── BuildingBlocks/
 │   ├── EventSourcing/
 │   ├── Outbox/
 │   ├── Observability/
 │   └── Messaging/
 │
 ├── Services/
 │   └── WaitingRoom/
 │       ├── Domain/
 │       │   ├── Aggregates/
 │       │   ├── Events/
 │       │   ├── ValueObjects/
 │       │   └── Invariants/
 │       │
 │       ├── Application/
 │       │   ├── Commands/
 │       │   ├── Handlers/
 │       │   ├── DTOs/
 │       │   └── Ports/
 │       │
 │       ├── Infrastructure/
 │       │   ├── EventStore/
 │       │   ├── Outbox/
 │       │   ├── Messaging/
 │       │   └── Persistence/
 │       │
 │       ├── Projections/
 │       │   ├── WaitingQueue/
 │       │   ├── Monitor/
 │       │   └── Rebuild/
 │       │
 │       └── API/
 │           ├── Endpoints/
 │           └── Contracts/
 │
 └── Tests/
     ├── Domain/
     ├── Application/
     ├── Projection/
     ├── Integration/
     └── E2E/

6. Arquitectura de Eventos — Monitor en Tiempo Real

Flujo:

Domain Event → Event Store → Outbox → Broker → Projection → Realtime Gateway → Monitor Screen


Realtime NO lee del Write Model.

Fan-out por:

Sala

Prioridad

Estado

Backpressure obligatorio.

7. Auditoría del Event Flow

Tu flujo debe cumplir:

Eventos ordenados por Aggregate

Idempotencia en consumers

Retry + DLQ

Outbox transaccional

Replay completo posible

Proyecciones reconstruibles

Si uno falla → inconsistencia sistémica.

8. Architecture Fitness Functions (Reglas ejecutables)

Debes poder verificar automáticamente:

Dominio

No referencias a Infra

Sin IO

Determinista

Eventos

Inmutables

Versionados

No editables

Proyecciones

Idempotentes

Replayables

Sistema

Replay completo consistente

Eventos duplicados no rompen

Docker reproducible

9. Reglas Anti-Corrupción (Anti-Degradación)
Nunca permitir

Lógica en Controllers

Lógica en Projections

DB en Domain

Eventos editables

Estado fuera del Event Store

Side effects síncronos

Realtime leyendo Write Model

Siempre exigir

Domain first

Eventos como fuente de verdad

Outbox transaccional

Idempotencia global

Replay posible

Observabilidad

Tests de Event Sourcing

Estado final

Si implementas lo anterior:

Arquitectura blindada

Event sourcing real

Escalable

Determinista

Reproducible

Lista para realtime serio

Lista para producción evolutiva
