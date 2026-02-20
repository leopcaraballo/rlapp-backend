# RLAPP — Domain Overview

**Descripción detallada de entidades, agregados, value objects y reglas de negocio.**

---

## 🎯 Dominio: WaitingRoom

El dominio modela la **gestión de colas de espera en atención sanitaria**.

### Problema de Negocio

Un hospital necesita:

- Gestionar múltiples colas de espera (general, especialidades)
- Priorizar pacientes (urgentes vs. rutinarios)
- Control de capacidad (no overflow)
- Trazabilidad completa (auditoría)
- Consultas rápidas del estado

### Solución Implementada

Un **event-sourced aggregate** que mantiene la cola y genera eventos para cada acción.

Estado funcional actual:

- Flujo estricto recepción → taquilla → consulta
- Estados alternos para pago pendiente, ausencias y cancelaciones
- Consultorios activos/inactivos con validación para llamada médica

---

## 🏛️ Agregado: WaitingQueue

### Responsabilidad

Proteger la **consistencia e invariantes** de una cola de espera.

### Estructura

```csharp
public sealed class WaitingQueue : AggregateRoot
{
    // Identity
    public string Id { get; private set; }                    // QUEUE-01
    public long Version { get; private set; }                 // Auto-increment

    // State
    public string QueueName { get; private set; }             // "Reception A"
    public int MaxCapacity { get; private set; }              // 20
    public List<WaitingPatient> Patients { get; private set; } // [PAT-001, PAT-002]
    public string? CurrentCashierPatientId { get; private set; }
    public string? CurrentAttentionPatientId { get; private set; }
    public IReadOnlyCollection<string> ActiveConsultingRooms { get; }

    // Audit
    public DateTime CreatedAt { get; private set; }
    public DateTime LastModifiedAt { get; private set; }

    // Factory
    public static WaitingQueue Create(
        string queueId,
        string queueName,
        int maxCapacity,
        EventMetadata metadata)  // For audit trail

    // Operations
    public void CheckInPatient(CheckInPatientRequest request)
    public string CallNextAtCashier(CallNextCashierRequest request)
    public void ValidatePayment(ValidatePaymentRequest request)
    public void MarkPaymentPending(MarkPaymentPendingRequest request)
    public void MarkAbsentAtCashier(MarkAbsentAtCashierRequest request)
    public void CancelByPayment(CancelByPaymentRequest request)
    public string ClaimNextPatient(ClaimNextPatientRequest request)
    public void CallPatient(CallPatientRequest request)
    public void CompleteAttention(CompleteAttentionRequest request)
    public void MarkAbsentAtConsultation(MarkAbsentAtConsultationRequest request)
    public void ActivateConsultingRoom(ActivateConsultingRoomRequest request)
    public void DeactivateConsultingRoom(DeactivateConsultingRoomRequest request)

    // Query
    public int CurrentCount => Patients.Count;
    public int AvailableCapacity => MaxCapacity - CurrentCount;
    public bool IsAtCapacity => CurrentCount >= MaxCapacity;

    // Event Handlers (private, invoked via reflection)
    private void When(WaitingQueueCreated @event) { ... }
    private void When(PatientCheckedIn @event) { ... }
}
```

### Métodos Públicos

#### `Create()`

**Caso de uso:** Crear nueva cola.

```csharp
var metadata = EventMetadata.CreateNew(
    aggregateId: "QUEUE-01",
    actor: "admin");

var queue = WaitingQueue.Create(
    queueId: "QUEUE-01",
    queueName: "Reception",
    maxCapacity: 20,
    metadata: metadata);
```

**Emite:** `WaitingQueueCreated event`

**Invariantes validadas:**

- Un nombre válido (no vacío, no nulo)
- Capacidad > 0

#### `CheckInPatient()`

**Caso de uso:** Paciente se registra en la cola.

```csharp
queue.CheckInPatient(
    patientId: PatientId.Create("PAT-001"),
    patientName: "Juan Pérez",
    priority: Priority.Create("High"),
    consultationType: ConsultationType.Create("General"),
    checkInTime: DateTime.UtcNow,
    metadata: EventMetadata.CreateNew(queue.Id, "nurse-001"),
    notes: "Asma aguda");
```

**Emite:** `PatientCheckedIn event`

**Invariantes validadas:**

- Cola no ha alcanzado capacidad máxima
- Paciente no está ya registrado (no duplicados)
- Priority es válido
- Name no es vacío
- QueuePosition >= 0

---

## 📚 Entidades

### WaitingPatient

**Contexto:** Solo existe dentro de WaitingQueue (no es agregado raíz).

```csharp
public sealed class WaitingPatient
{
    // Identity (within aggregate)
    public PatientId PatientId { get; }

    // State
    public string PatientName { get; }
    public Priority Priority { get; }
    public ConsultationType ConsultationType { get; }
    public string? Notes { get; }
    public DateTime CheckInTime { get; }
    public int QueuePosition { get; }

    // Constructor (public - created after domain validation)
    public WaitingPatient(
        PatientId patientId,
        string patientName,
        Priority priority,
        ConsultationType consultationType,
        DateTime checkInTime,
        int queuePosition,
        string? notes = null)

    // Queries
    public TimeSpan GetWaitDuration(DateTime currentTime)
        => currentTime - CheckInTime;
}
```

**Nota:** No tiene métodos de comportamiento. Es un contenedor de datos con invariantes de construcción.

---

## 💎 Value Objects

### PatientId

Envuelve un ID de paciente con validación.

```csharp
public sealed record PatientId
{
    public string Value { get; }  // "PAT-001"

    public static PatientId Create(string value)
        // Throws DomainException si value es null/empty
        // Retorna PatientId(value.Trim())
}
```

**Invariantes:**

- No puede ser vacío
- Automáticamente trimmed

**Por qué Value Object?**

- Type safety (no confundir con otros IDs)
- Invariantes centralizadas
- Comportamiento compartido

### WaitingQueueId

```csharp
public sealed record WaitingQueueId
{
    public string Value { get; }  // "QUEUE-01"

    public static WaitingQueueId Create(string value)
        // Throws DomainException si value es null/empty
}
```

### Priority

Enumera niveles de prioridad con conversión normalizada.

```csharp
public sealed record Priority
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Urgent = "Urgent";

    private static readonly HashSet<string> ValidValues =
        [Low, Medium, High, Urgent];

    public string Value { get; }

    public static Priority Create(string value)
    {
        // Normaliza entrada: "high" → "High"
        // Throws DomainException si no es válido
        // Retorna Priority(canonical)
    }

    // Nivel numérico para comparaciones
    public int Level => Value switch
    {
        Urgent => 4,
        High => 3,
        Medium => 2,
        Low => 1,
        _ => 0
    };
}
```

**Invariantes:**

- Solo valores permitidos
- Normalizado (case-insensitive → canonical)
- Comparable numéricamente

### ConsultationType

```csharp
public sealed record ConsultationType
{
    private static readonly HashSet<string> DefaultTypes =
        ["General", "Cardiology", "Oncology", "Pediatrics", "Surgery"];

    public string Value { get; }

    public static ConsultationType Create(string value)
    {
        // Valida no vacío
        // Valida length 2-100
        // Throws DomainException si inválido
    }
}
```

**Invariantes:**

- No puede ser vacío
- máx. 100 caracteres
- mín. 2 caracteres

**Diseño:** Aceptable (Cardiology, Pediatrics, etc.) pero no limitado a whitelist (extensible).

---

## 📋 Eventos de Dominio

### WaitingQueueCreated

Registra la creación de una cola.

```csharp
public sealed record WaitingQueueCreated : DomainEvent
{
    public required string QueueId { get; init; }
    public required string QueueName { get; init; }
    public required int MaxCapacity { get; init; }
    public required DateTime CreatedAt { get; init; }

    public override string EventName => "WaitingQueueCreated";

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();  // Check metadata
        // Validar QueueId, QueueName, MaxCapacity > 0
    }
}
```

**Emitido por:** `WaitingQueue.Create()`

**Procesado por:** `WaitingRoomProjectionEngine` (initial state)

### PatientCheckedIn

Registra que un paciente se ha registrado.

```csharp
public sealed record PatientCheckedIn : DomainEvent
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required string ConsultationType { get; init; }
    public required int QueuePosition { get; init; }
    public required DateTime CheckInTime { get; init; }
    public string? Notes { get; init; }

    public override string EventName => "PatientCheckedIn";

    protected override void ValidateInvariants()
    {
        base.ValidateInvariants();
        // Validar todos los required fields
    }
}
```

**Emitido por:** `WaitingQueue.CheckInPatient()`

**Procesado por:**

- `PatientCheckedInProjectionHandler` → actualiza vistas
- Lag tracker → registra métricas

---

## 📋 Invariantes

**Archivo:** `WaitingRoom.Domain/Invariants/WaitingQueueInvariants.cs`

### 1. ValidateCapacity

```csharp
public static void ValidateCapacity(int currentCount, int maxCapacity)
{
    if (currentCount >= maxCapacity)
        throw new DomainException(
            $"Queue is at maximum capacity ({maxCapacity}). Cannot add more patients.");
}
```

**Cuándo:** Antes de `CheckInPatient()`

**Qué protege:** No pueden haber más pacientes que la capacidad máxima.

### 2. ValidateDuplicateCheckIn

```csharp
public static void ValidateDuplicateCheckIn(
    string patientId,
    IEnumerable<string> queuedPatientIds)
{
    if (queuedPatientIds.Contains(patientId))
        throw new DomainException(
            $"Patient {patientId} is already in the queue.");
}
```

**Cuándo:** Antes de `CheckInPatient()`

**Qué protege:** El mismo paciente no puede estar dos veces en la cola.

### 3. ValidatePriority

```csharp
public static void ValidatePriority(string priority)
{
    var validPriorities = new[] { "low", "medium", "high", "urgent" };
    var normalized = priority.Trim().ToLowerInvariant();

    if (!validPriorities.Contains(normalized))
        throw new DomainException($"Invalid priority: {priority}");
}
```

**Cuándo:** Antes de `CheckInPatient()`

**Qué protege:** Solo prioridades válidas.

### 4. ValidateQueueName

```csharp
public static void ValidateQueueName(string queueName)
{
    if (string.IsNullOrWhiteSpace(queueName))
        throw new DomainException("Queue name cannot be empty");
}
```

**Cuándo:** En `WaitingQueue.Create()`

**Qué protege:** Toda cola debe tener nombre.

---

## 📊 Metadatos de Eventos

Cada evento lleva `EventMetadata` para trazabilidad:

```csharp
public sealed record EventMetadata
{
    // Identificación
    public string EventId { get; init; }                  // UUID
    public string AggregateId { get; init; }             // El ID del agregado
    public long Version { get; init; }                   // Auto-incrementa

    // Trazabilidad Distribuida
    public string CorrelationId { get; init; }          // ID de request completo
    public string CausationId { get; init; }            // ID del comando que lo causó

    // Auditoría
    public string Actor { get; init; }                   // "nurse-001", "system"
    public DateTime OccurredAt { get; init; }           // Timestamp UTC

    // Idempotencia
    public string IdempotencyKey { get; init; }         // Previene duplicados si se reintente

    // Evolución
    public int SchemaVersion { get; init; }             // Para manejar cambios de schema
}
```

**Ejemplo:**

```json
{
  "eventId": "e47ac10b-58cc-4372-a567-0e02b2c3d479",
  "aggregateId": "QUEUE-01",
  "version": 3,
  "correlationId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "causationId": "c47ac10b-58cc-4372-a567-0e02b2c3d479",
  "actor": "nurse-001",
  "occurredAt": "2026-02-19T10:00:00Z",
  "idempotencyKey": "retry-12345",
  "schemaVersion": 1
}
```

---

## 🎬 Secuencias de Eventos

### Secuencia 1: Crear Cola

```
Time: 10:00:00
┌─────────────────────────────────────────┐
│ WaitingQueueCreated                      │
├─────────────────────────────────────────┤
│ QueueId: QUEUE-01                        │
│ QueueName: "Reception A"                 │
│ MaxCapacity: 20                          │
│ CreatedAt: 2026-02-19T10:00:00Z         │
│ Metadata: {                              │
│   actor: "admin",                        │
│   version: 1,                            │
│   correlationId: "corr-1"                │
│ }                                        │
└─────────────────────────────────────────┘
     ↓ PERSISTED
     ↓ PUBLISHED TO PROJECTIONS
  Queue state: { Id: "QUEUE-01", Patients: [] }
```

### Secuencia 2: Check-In de Paciente

```
Time: 10:05:00
┌─────────────────────────────────────────┐
│ PatientCheckedIn (Event #1)             │
├─────────────────────────────────────────┤
│ QueueId: QUEUE-01                        │
│ PatientId: PAT-001                       │
│ PatientName: "Juan Pérez"                │
│ Priority: "High"                         │
│ ConsultationType: "General"              │
│ QueuePosition: 0                         │
│ CheckInTime: 2026-02-19T10:05:00Z       │
│ Metadata: { version: 2, actor: "nurse" }│
└─────────────────────────────────────────┘
     ↓ PERSISTED (aggregate version → 2)
     ↓ OUTBOX stored
     ↓ EventStore: [QueueCreated, PatientCheckedIn]
     │
     ↓ (async) OUTBOX WORKER
     ↓ PUBLISHED TO RABBITMQ
     │
     ↓ PROJECTIONS UPDATED
     Queue state: {
       Patients: [
         { PatientId: "PAT-001", Priority: "High", CheckInTime: ... }
       ]
     }
```

### Secuencia 3: Múltiples Check-Ins

```
Event Flow:
QueueCreated (v1)
  ↓
PatientCheckedIn "PAT-001" (v2)
  ↓
PatientCheckedIn "PAT-002" (v3)
  ↓
PatientCheckedIn "PAT-003" (v4)

Aggregate State After Each Event:
v1: { Patients: [] }
v2: { Patients: [PAT-001] }
v3: { Patients: [PAT-001, PAT-002] }
v4: { Patients: [PAT-001, PAT-002, PAT-003] }
```

---

## 🔄 Flujo de Estado

### Estados de un Agregado

```
┌──────────────────┐
│  Aggregate Loaded│  (from EventStore)
└────────┬─────────┘
         │
         ├─→ Apply Event 1
         │   (When(Event1) called via reflection)
         │   State updates
         │
         ├─→ Apply Event 2
         │   State updates
         │   ...
         │
         └─→ Current State = Aggregated result
             (All events applied in order)
```

### Ejemplo Concreto

```csharp
// Load: Replay events
var events = [
    WaitingQueueCreated { QueueId: "Q1", MaxCapacity: 20 },
    PatientCheckedIn { PatientId: "P1", Priority: "High" },
    PatientCheckedIn { PatientId: "P2", Priority: "Low" }
];

var queue = AggregateRoot.LoadFromHistory<WaitingQueue>("Q1", events);

// Result:
queue.Id = "Q1"
queue.MaxCapacity = 20
queue.Version = 3
queue.Patients.Count = 2
  queue.Patients[0].PatientId = "P1", Priority = "High"
  queue.Patients[1].PatientId = "P2", Priority = "Low"
```

---

## 🎯 Reglas de Negocio Explícitas

| Regla | Implementación | Nivel |
|-------|----------------|-------|
| Queue name must not be empty | `ValidateQueueName()` | Invariant |
| MaxCapacity must be > 0 | `Create()` constructor | Invariant |
| Priority must be from {Low, Medium, High, Urgent} | `Priority.Create()` | ValueObject |
| Patient cannot check in twice | `ValidateDuplicateCheckIn()` | Invariant |
| Queue cannot exceed MaxCapacity | `ValidateCapacity()` | Invariant |
| Patient name must not be empty | `WaitingPatient` constructor | Entity |
| Patient name is trimmed on storage | Constructor | Entity |
| QueuePosition cannot be negative | `WaitingPatient` constructor | Entity |
| ConsultationType length must be 2-100 | `ConsultationType.Create()` | ValueObject |

---

## 🚫 Excepciones de Dominio

```csharp
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
```

**Cuándo se lanza:**

- Cualquier violación de invariante
- Prioridad inválida
- Capacidad excedida
- Paciente duplicado
- Nombre vacío

**Ejemplo:**

```csharp
throw new DomainException(
    $"Queue is at maximum capacity ({maxCapacity}). Cannot add more patients.");
```

---

## 📐 Decisiones de Diseño de Dominio

| Decisión | Justificación | Alternativa |
|----------|--------------|------------|
| **WaitingQueue es agregado raíz** | Protege invariantes de cola | Múltiples agregados sin consistencia |
| **WaitingPatient dentro del agregado** | Cohesión (siempre accedido con cola) | Agregado separado (complejidad) |
| **Priority es Value Object** | Type-safe, validación centralizada | String en event |
| **Events son records (immutable)** | Garantiza no-mutation | Classes (riesgo de mutation) |
| **Metadatos en cada evento** | Auditoría, tracing, idempotencia | Sin metadata (lost info) |
| **Metadata.CreateNew() factory** | Consistencia en creación | Construcción manual (olvido) |
| **Version en agregado** | Detección de conflictos concurrentes | Sin versioning (race conditions) |

---

**Última actualización:** Febrero 2026
