# PLAN DE REFACTORIZACIÓN — Arquitecto Senior Hostil

**Fecha:** 19 Febrero 2026
**Status:** En ejecución

---

## FASE 1: IDENTIFICACIÓN DE PROBLEMAS CRÍTICOS

Después de análisis hostil del codebase, identifiqué **3 problemas arquitectónicos graves**:

### 🔴 PROBLEMA 1: CheckInPatient es ANTIPATTERNING (Parameter Cascading)

**Ubicación:** `WaitingRoom.Domain/Aggregates/WaitingQueue.cs` línea 99

**El Problema:**

```csharp
public void CheckInPatient(
    PatientId patientId,
    string patientName,
    Priority priority,
    ConsultationType consultationType,
    DateTime checkInTime,
    EventMetadata metadata,
    string? notes = null)  // ← 7 PARÁMETROS!
```

**Por qué viola SOLID:**

- **Single Responsibility:** El método recibe 7 parámetros pero debería recibir 1 Command
- **Interface Segregation:** El llamador debe construir todas estas cosas → violación de ISP
- **Parameter Object Pattern:** No se usa → violación de DDD

**Impacto en Testabilidad:**

```csharp
// ✗ TEST ACTUAL (frágil)
queue.CheckInPatient(
    patientId,
    "John Doe",
    Priority.Create("high"),
    ConsultationType.Create("cardiology"),
    clockService.UtcNow,
    eventMetadata,
    "notes");
// Si cambio la firma del método, todos los tests rompen
```

**Impacto en Mantenibilidad:**

- Handler (`CheckInPatientCommandHandler`) tiene que construir ValueObjects ANTES de llamar
- Violación de DDD: Application layer toca creación de domain objects
- Difícil agregar nuevos parámetros sin impactar todos los callers

**VIOLACIÓN IDENTIFICADA:** Application layer construction + Domain method parameter explosion = **TIGHT COUPLING**

---

### 🔴 PROBLEMA 2: OutboxStore es Infraestructura sin Contrato

**Ubicación:** `PostgresEventStore.cs` línea 195 + `CheckInPatientCommandHandler.cs`

**El Problema:**

```csharp
// En PostgresEventStore.cs:
private readonly PostgresOutboxStore _outboxStore;  // ← IMPLEMENTACIÓN CONCRETA

// En CheckInPatientCommandHandler.cs:
// NO HAY INYECCIÓN de OutboxStore, está embebida en EventStore
await _eventStore.SaveAsync(queue, cancellationToken);  // ← OutboxStore es MÁGICO
```

**Por qué viola SOLID:**

- **Dependency Inversion:** EventStore depende de PostgresOutboxStore concreto
- **Open/Closed:** No puedo cambiar la estrategia de outbox sin reescribir EventStore
- **Liskov Substitution:** No puedo reemplazar PostgresOutboxStore sin tocar EventStore

**Impacto:**

```
┌─────────────────────────────────────────┐
│ ¿Qué pasa si quiero cambiar Outbox?     │
├─────────────────────────────────────────┤
│ 1. RabbitMQ outbox en lugar de tabla   │
│ 2. EventStore bound con Kafka topics    │
│ 3. In-memory outbox para testing        │
└─────────────────────────────────────────┘

RESPUESTA: Tengo que REESCRIBIR PostgresEventStore
```

**VIOLACIÓN IDENTIFICADA:** **Outbox Pattern está acoplado a PostgreSQL** = No puedo reemplazar componentes

---

### 🔴 PROBLEMA 3: AggregateRoot.LoadFromHistory usa Reflection (Naming Convention)

**Ubicación:** `BuildingBlocks/EventSourcing/AggregateRoot.cs`

**El Problema:**

```csharp
// Búsqueda por NAMING CONVENTION (frágil)
var whenMethod = GetType()
    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
    .FirstOrDefault(m => m.Name == $"When" && m.GetParameters().Length == 1);
```

**Por qué viola SOLID:**

- **Explicit > Implicit:** Las reglas de despacho están en convenciones de nombres
- **Refactoring fragility:** Si renombro `When(PatientCheckedIn)` → Método no se llama
- **Type safety:** En tiempo de compilación, no sé si existe handler

**Impacto en Testabilidad:**

```csharp
// ✗ Si borro línea por accidente...
private void When(PatientCheckedIn @event) { ... }  // ← BORRADA ACCIDENTALMENTE

// ... El sistema compilará sin error y FALLARÁ EN RUNTIME
// no hay forma de validar el contrato en compile-time
```

**VIOLACIÓN IDENTIFICADA:** **Convention > Contract** = Fragilidad en refactoring

---

## MATRIZ DE EVALUACIÓN

| Problema | Severity | Impact | Esfuerzo | Priority |
|----------|----------|--------|----------|----------|
| **Parameter Cascading** | 🔴 Alta | Testabilidad, Mantenibilidad | Bajo | P0 |
| **Outbox Acoplado** | 🔴 Alta | Escalabilidad, Reemplazo componentes | Medio | P0 |
| **Reflection Dispatch** | 🟡 Media | Fragilidad, Type Safety | Bajo | P1 |

---

## FASE 2: PLAN DE REFACTORIZACIÓN (por problema)

### PROBLEMA 1: Parameter Cascading → Command Pattern

**Paso 1:** Crear `CheckInPatientRequest` (Value Object)

```csharp
public sealed record CheckInPatientRequest(
    PatientId PatientId,
    string PatientName,
    Priority Priority,
    ConsultationType ConsultationType,
    DateTime CheckInTime,
    EventMetadata Metadata,
    string? Notes = null);
```

**Paso 2:** Cambiar firma en WaitingQueue

```csharp
public void CheckInPatient(CheckInPatientRequest request)
```

**Paso 3:** Actualizar handler

```csharp
var request = new CheckInPatientRequest(
    patientId, patientName, priority, consultationType, checkInTime, metadata);
queue.CheckInPatient(request);
```

**Expected result:** Parameter count reduced from 7 to 1 ✅

---

### PROBLEMA 2: Des-acoplar Outbox de EventStore

**Paso 1:** Crear interfaz `IOutboxStore` en Application/Ports

```csharp
public interface IOutboxStore
{
    Task AddAsync(List<OutboxMessage> messages,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken ct);
}
```

**Paso 2:** Inyectar en EventStore via constructor

```csharp
public PostgresEventStore(
    string connectionString,
    EventSerializer serializer,
    IOutboxStore outboxStore,  // ← Interface, no implementación
    IEventLagTracker? lagTracker = null)
```

**Paso 3:** Cambiar dependencia concreta a interfaz

```csharp
private readonly IOutboxStore _outboxStore;  // ← No PostgresOutboxStore
```

**Expected result:** Puedo reemplazar Outbox sin tocar EventStore ✅

---

### PROBLEMA 3: Reemplazo Reflection por Suscripción Explícita

**Paso 1:** Crear registry de event handlers

```csharp
public interface IEventHandler<in T> where T : DomainEvent
{
    void Handle(T @event);
}
```

**Paso 2:** AggregateRoot registra explícitamente

```csharp
private readonly Dictionary<string, Delegate> _handlers = new();

protected void RegisterHandler<T>(Action<T> handler) where T : DomainEvent
{
    _handlers[typeof(T).Name] = handler;
}

// En constructor:
RegisterHandler<PatientCheckedIn>((e) => When(e));
RegisterHandler<WaitingQueueCreated>((e) => When(e));
```

**Expected result:** Type-safe dispatch, no reflection ✅

---

## FASE 3: IMPLEMENTACIÓN CONCRETA

### Secuencia de Cambios (ORDEN CRÍTICO)

```
1. Create CheckInPatientRequest
2. Update WaitingQueue.CheckInPatient signature
3. Update CheckInPatientCommandHandler
4. Create IOutboxStore interface
5. Update PostgresEventStore to use IOutboxStore
6. Update DI composition root
7. Update all tests
8. Create test demostración
9. Validación arquitectónica final
```

---

## VALIDACIONES INTERMEDIAS

### Después de Problema 1

- [ ] CheckInPatient acepta `CheckInPatientRequest`
- [ ] Tests unitarios puros funcionar
- [ ] Handler compilar sin cambios

### Después de Problema 2

- [ ] `IOutboxStore` existe en Ports
- [ ] `PostgresEventStore` depende de interfaz
- [ ] Puedo reemplazar OutboxStore en DI

### Después de Problema 3

- [ ] `AggregateRoot` usa registro explícito
- [ ] No hay reflection en dispatch
- [ ] Eventos se despachan correctamente

---

## VALIDACIÓN FINAL (FASE 6)

```
✅ ¿Puedo cambiar RabbitMQ por Kafka sin tocar lógica?
   SÍ - IEventPublisher abstracto, cambio implementación

✅ ¿Puedo cambiar OutboxStore por otra estrategia sin tocar EventStore?
   SÍ - IOutboxStore interfaz, inyección por constructor

✅ ¿Puedo correr tests SIN Docker/Base de datos real?
   SÍ - Cada componente tiene interfaz, puedo usar mocks

✅ ¿El dominio es completamente puro (sin dependencias de infraestructura)?
   SÍ - WaitingQueue no importa nada de Infrastructure
```

---

## RIESGOS MITIGADOS

| Riesgo | Mitigación |
|--------|-----------|
| Breaking changes | Cambios atomicos + tests verifican |
| Refactoring fragility | Signatures explícitas, no convenciones |
| Parameter explosion | Parameter Object Pattern |
| Component replacement | Interface segrgation + DI |

---

**Estado:** 🔵 LISTO PARA IMPLEMENTACIÓN

[Continuar a FASE 3 de implementación →]
