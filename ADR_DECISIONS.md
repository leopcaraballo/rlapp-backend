# ADR-001: Parameter Object Pattern for Domain Aggregates

**Date:** 2026-02-19
**Status:** ACCEPTED
**Context:** Refactorización de WaitingQueue.CheckInPatient()
**Decision Makers:** Arquitecto Senior (Enterprise Mode)

---

## Problem

**Original Signature:**

```csharp
public void CheckInPatient(
    PatientId patientId,
    string patientName,
    Priority priority,
    ConsultationType consultationType,
    DateTime checkInTime,
    EventMetadata metadata,
    string? notes = null)  // ← 7 parámetros
```

### Impacts

1. **Parameter Cascading Anti-pattern:** Application layer construye 7 objetos antes de llamar
2. **Testing Fragility:** Cambiar firma rompía todos los tests
3. **Extension Difficulty:** Agregar parámetro requería actualizar todos los callers
4. **Lack of Intent:** No está claro que los 7 parámetros forman un "request coherente"

---

## Decision

**Implementar Parameter Object Pattern:**

```csharp
public sealed record CheckInPatientRequest
{
    public PatientId PatientId { get; init; }
    public string PatientName { get; init; }
    public Priority Priority { get; init; }
    public ConsultationType ConsultationType { get; init; }
    public DateTime CheckInTime { get; init; }
    public EventMetadata Metadata { get; init; }
    public string? Notes { get; init; }
}

public void CheckInPatient(CheckInPatientRequest request)  // ← 1 parámetro
```

### Rationale

1. **Encapsulation:** Agrupar parámetros relacionados en un objeto nombrado
2. **Extensibility:** Agregar campos al request sin cambiar método
3. **Testability:** Simplifica test setup, reduce boilerplate
4. **Intent:** El nombre `CheckInPatientRequest` es autoexplicativo
5. **DDD:** Representa un concepto del dominio (una solicitud coherente)

---

## Consequences

### Positive

- ✅ Tests más simples (1 create vs 7 parameters)
- ✅ Extensible: agregar campos no rompe tests
- ✅ Cleaner code: intent es claro
- ✅ Type-safe: compiler valida todos los campos
- ✅ Reusable: request puede reutilizarse en tests

### Negative

- ❌ Más clases: +1 clase (CheckInPatientRequest)
- ❌ Constructor verboso en algunos casos (pero init-only, así es legible)

### Neutral

- → API no cambia (still accepts command → maps to request)
- → Handler crea request internamente

---

## Trade-offs

| Option | Pros | Cons |
|--------|------|------|
| **Parameter Object (Selected)** | Extensible, testable, clear intent | +1 clase |
| **Method Overloading** | No new class | Confusing, hard to extend |
| **Builder Pattern** | Flexible | Overkill para 7 parámetros |
| **Tuple** | Simple, lightweight | No type safety, no names |

---

## Alternatives Considered

### 1. Keep as-is (7 params)

- ❌ Testability fragile
- ❌ Can't extend without breaking

### 2. Builder Pattern

```csharp
builder.WithPatientId(...).WithPriority(...).Build()
```

- ❌ Overkill
- ❌ Más complejo que Parameter Object
- ✅ Flexible pero no necesitamos esa flexibilidad

### 3. Tuple

```csharp
public void CheckInPatient((PatientId PatientId, string PatientName, ...) request)
```

- ❌ No hay type safety al acceder campos
- ❌ Los names son ficticios (compiler ignora)
- ✅ Lightweight

---

## Implementation

### File Structure

```
src/Services/WaitingRoom/
└─ WaitingRoom.Domain/
   └─ Commands/
      └─ CheckInPatientRequest.cs  ← NEW
```

### Code Changes

1. **Created:** CheckInPatientRequest.cs
2. **Modified:** WaitingQueue.cs (signature change)
3. **Modified:** CheckInPatientCommandHandler.cs (build request)
4. **Modified:** Tests (use factory helper)

### Backward Compatibility

- ✅ No breaking changes for API consumers
- ✅ Handler internal only
- ⚠️ If directly calling WaitingQueue.CheckInPatient() → Must update to CheckInPatientRequest

---

## Acceptance Criteria

- [x] CheckInPatientRequest implementado
- [x] WaitingQueue.CheckInPatient(CheckInPatientRequest) compila
- [x] Tests domain funcionan sin cambios
- [x] Handler compilar y funcionar
- [x] Documentación actualizada

---

## Related Decisions

- ADR-002: IOutboxStore Interface Segregation
- Architecture: Hexagonal + Event Sourcing + CQRS
- Pattern: Parameter Object (GoF)

---

# ADR-002: Interface Segregation for OutboxStore

**Date:** 2026-02-19
**Status:** ACCEPTED
**Context:** De-coupling PostgresEventStore from PostgresOutboxStore
**Decision Makers:** Arquitecto Senior (Enterprise Mode)

---

## Problem

**Original Coupling:**

```csharp
// En PostgresEventStore:
private readonly PostgresOutboxStore _outboxStore;  // ← CONCRETE CLASS

public PostgresEventStore(
    string connectionString,
    EventSerializer serializer,
    PostgresOutboxStore outboxStore,  // ← Concrete dependency
    IEventLagTracker? lagTracker = null)
```

### Impacts

1. **Tight Coupling:** EventStore depends on PostgresOutboxStore implementation
2. **No Flexibility:** Cannot change outbox strategy without rewriting EventStore
3. **Hard to Test:** InMemoryOutboxStore would require different EventStore
4. **Violates DIP:** Depends on concrete class, not abstraction

---

## Decision

**Introduce IOutboxStore interface in Application/Ports:**

```csharp
// WaitingRoom.Application/Ports/IOutboxStore.cs
public interface IOutboxStore
{
    Task AddAsync(
        List<OutboxMessage> messages,
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken = default);
}

// In PostgresEventStore:
private readonly IOutboxStore _outboxStore;  // ← INTERFACE, not class
```

### Rationale

1. **Inversion of Control:** EventStore depends on abstraction, not implementation
2. **Flexibility:** Can swap OutboxStore strategies without changing EventStore
3. **Testability:** Can mock IOutboxStore in EventStore tests
4. **Scalability:** EventStore remains unchanged if outbox strategy changes

---

## Consequences

### Positive

- ✅ OutboxStore is now replaceable
- ✅ Violates Dependency Inversion (now follows DIP)
- ✅ Testability improved
- ✅ Future: Can implement in-memory, Kafka-based, etc.

### Negative

- ❌ More interfaces to maintain
- ❌ Slight indirection in code

---

## Alternatives Considered

### 1. Keep concrete dependency (PostgresOutboxStore)

- ❌ No flexibility
- ❌ Violates DIP

### 2. Generic interface with type parameter

```csharp
public interface IOutboxStore<T> { }
```

- ❌ Unnecessary generics
- ✅ But adds complexity

### 3. Service locator

```csharp
_outboxStore = ServiceLocator.GetOutboxStore();
```

- ❌ Anti-pattern
- ❌ No dependency injection

---

## Implementation

### File Structure

```
src/Services/WaitingRoom/
├─ WaitingRoom.Application/
│  └─ Ports/
│     └─ IOutboxStore.cs  ← NEW INTERFACE
└─ WaitingRoom.Infrastructure/
   └─ Persistence/
      ├─ EventStore/
      │  └─ PostgresEventStore.cs  (modified)
      └─ Outbox/
         └─ PostgresOutboxStore.cs  (modified)
```

### DI Registration

```csharp
// Program.cs
services.AddSingleton<PostgresOutboxStore>();
services.AddSingleton<IOutboxStore>(sp => sp.GetRequiredService<PostgresOutboxStore>());

services.AddSingleton<IEventStore>(sp =>
    new PostgresEventStore(
        connectionString,
        sp.GetRequiredService<EventSerializer>(),
        sp.GetRequiredService<IOutboxStore>(),  // ← Interface injection
        sp.GetRequiredService<IEventLagTracker>()));
```

---

## Acceptance Criteria

- [x] IOutboxStore.cs created in Application/Ports
- [x] PostgresEventStore depends on IOutboxStore
- [x] PostgresOutboxStore implements IOutboxStore
- [x] DI registration correct
- [x] Tests still pass

---

## Future Extensions

### Option 1: InMemory Outbox

```csharp
public class InMemoryOutboxStore : IOutboxStore { }
// Para testing sin BD
```

### Option 2: Kafka-based Outbox

```csharp
public class KafkaOutboxStore : IOutboxStore { }
// Para escalabilidad, replace polling
```

### Option 3: Event Broker Integration

```csharp
public class RedisOutboxStore : IOutboxStore { }
// Para alta disponibilidad
```

---

## Related Decisions

- ADR-001: Parameter Object Pattern
- Architecture: Hexagonal + Event Sourcing + CQRS
- Principle: Dependency Inversion Principle (SOLID)

---

# ADR-003: Deferred - Reflection Dispatch to Registry Pattern

**Date:** 2026-02-19
**Status:** DEFERRED
**Priority:** P2 (Future)
**Context:** AggregateRoot event dispatch via reflection

---

## Problem Identified

```csharp
// Current: Reflection-based dispatch
var whenMethod = GetType()
    .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
    .FirstOrDefault(m => m.Name == "When" && m.GetParameters().Length == 1);
```

### Issues

1. **Naming Convention:** Depends on convention (When method must exist)
2. **Runtime Discovery:** No compile-time validation
3. **Performance:** Reflection overhead
4. **Type Safety:** Can't validate handler exists at compile-time

### Impact Assessment

- **Severity:** 🟡 Medium
- **Probability:** 🟢 Low (convention well-known)
- **Consequence:** Runtime error if handler missing

---

## Decision: Deferred

**Why not now:**

1. ✓ Convention is well-documented
2. ✓ Low risk (all handlers are present)
3. ✓ High effort for marginal gain
4. ✓ Registry pattern is invasive change

**When to implement (v2.0):**

- When event handling becomes more complex
- When supporting multiple handlers per event
- When performance becomes critical

---

## Planned Solution (v2.0)

```csharp
public interface IEventHandler<in T> where T : DomainEvent
{
    void Handle(T @event);
}

public abstract class AggregateRoot
{
    private readonly Dictionary<Type, Delegate> _handlers = new();

    protected void RegisterHandler<T>(Action<T> handler) where T : DomainEvent
    {
        _handlers[typeof(T)] = handler;
    }

    protected virtual void ApplyEvent(DomainEvent @event)
    {
        var type = @event.GetType();
        if (_handlers.TryGetValue(type, out var handler))
        {
            handler.DynamicInvoke(@event);
        }
        else
        {
            throw new InvalidOperationException($"No handler for {type.Name}");
        }
    }
}

// In WaitingQueue:
public WaitingQueue()
{
    RegisterHandler<PatientCheckedIn>((e) => When(e));
    RegisterHandler<WaitingQueueCreated>((e) => When(e));
}
```

### Benefits of Registry Approach

- ✅ Type-safe: compile-time validation
- ✅ Explicit: handlers are registered in constructor
- ✅ No reflection: performance improvement
- ✅ Debuggable: stack traces are clear

---

## Decision Rationale

**Fix now:** High impact, low effort

- ✅ Parameter Object: Critical, easy
- ✅ Interface Segregation: Critical, easy

**Fix later:** Deferred value, high effort

- 🟡 Reflection Registry: Marginal value, invasive change

---

## Related ADRs

- ADR-001: Parameter Object Pattern
- ADR-002: Interface Segregation
- Principle: Explicit > Implicit (Zen of Python)

---

## Summary

| ADR | Decision | Status | Impact |
|-----|----------|--------|--------|
| ADR-001 | Parameter Object | ✅ ACCEPTED | High |
| ADR-002 | Interface Segregation | ✅ ACCEPTED | High |
| ADR-003 | Reflection Registry | 🟡 DEFERRED | Medium |

All decisions respect SOLID principles and Clean Architecture.
