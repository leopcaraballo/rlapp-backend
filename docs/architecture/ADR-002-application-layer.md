# ADR-002: Application Layer Design with CQRS + Event Sourcing

**Date:** 2026-02-19
**Status:** ACCEPTED
**Authors:** Architecture Team
**Related:** PHASE-2 Implementation

## Context

The RLAPP system requires a clear, maintainable orchestration layer that:

1. Coordinates domain operations
2. Manages Event Sourcing persistence
3. Publishes events for subscribers
4. Remains independent of infrastructure

Traditional layered architecture often mixes concerns (e.g., business logic in services). We needed explicit separation between pure domain logic and orchestration.

## Decision

Implement Application Layer as pure orchestration using CQRS + Event Sourcing pattern:

### 1. Command Pattern

```
┌─────────────────┐
│  Input Object   │
│   (Command)     │
└────────┬────────┘
         │
    ┌────▼──────────────┐
    │  CommandHandler   │
    │  (Orchestration)  │
    └────┬─────────────┘
         │
    ┌────▼──────────────┐
    │  Aggregate        │
    │  (Domain Logic)   │
    └────┬─────────────┘
         │
    ┌────▼──────────────┐
    │  DomainEvent      │
    │  (Fact)           │
    └────┬─────────────┘
         │
    ┌────▼──────────────┐
    │  EventStore       │
    │  (Persistence)    │
    └──────────────────┘
```

### 2. Separation of Concerns

**Commands** define intent:

```csharp
public sealed record CheckInPatientCommand
{
    public required string QueueId { get; init; }
    public required string PatientId { get; init; }
    // ... input data only
}
```

**Handlers** orchestrate execution:

```csharp
public async Task<int> HandleAsync(CheckInPatientCommand command)
{
    var queue = await _eventStore.LoadAsync(command.QueueId);
    queue.CheckInPatient(...);
    await _eventStore.SaveAsync(queue);
    await _eventPublisher.PublishAsync(queue.UncommittedEvents);
    return queue.UncommittedEvents.Count;
}
```

**Domain** enforces rules:

```csharp
public void CheckInPatient(CheckInPatientRequest request)
{
    if (CurrentCount >= MaxCapacity)
        throw new DomainException("Queue at capacity");
    // ... generate event
    RaiseEvent(new PatientCheckedIn { ... });
}
```

### 3. Dependency Inversion with Ports

```
Application
    ├─ IEventStore (port)
    ├─ IEventPublisher (port)
    │
Infrastructure (Phase 3)
    ├─ SqlEventStore (implementation)
    └─ RabbitMQPublisher (implementation)
```

Each port is an interface:

- Application depends on abstraction
- Infrastructure implements concrete version
- Swappable without changing Application

### 4. No Platform Dependencies

Application layer contains NO:

- ❌ Database access
- ❌ HTTP calls
- ❌ Message publishing implementation
- ❌ Configuration reading
- ❌ Logging directives
- ❌ ORM references
- ❌ Framework-specific code

**Result:** Application runs pure in-memory, testable without infrastructure.

## Consequences

### Positive ✅

1. **Pure Testability**
   - Unit tests use mocks only
   - No database needed
   - Tests run in milliseconds
   - 100% coverage possible

2. **Clear Boundaries**
   - Every layer has explicit responsibility
   - Easy to understand data flow
   - No hidden coupling
   - Safe refactoring

3. **Infrastructure Independence**
   - Can swap database, message broker, cache
   - Without changing Application or Domain
   - Multiple implementations possible

4. **Determinism**
   - Same command with same aggregate = same events
   - Replay from history is reproducible
   - Event sourcing audit trail

5. **Observability**
   - Commands are traceable artifacts
   - Events are labeled with correlation ID
   - Causation chain explicit
   - Full domain event audit available

6. **Scalability**
   - Commands can be queued
   - Handlers can be parallelized
   - Events enable event-driven subscribers
   - CQRS enables read/write optimization

### Negative/Tradeoffs ⚠️

1. **More Classes**
   - CheckInPatientCommand
   - CheckInPatientCommandHandler
   - CheckInPatientDto
   - More than traditional services

2. **Mapping Overhead**
   - HTTP body → DTO → Command → Domain
   - Requires mapper logic
   - Extra layers but explicit boundaries

3. **Learning Curve**
   - Developers must understand CQRS
   - Port abstraction pattern
   - Event sourcing concepts

4. **Initial Complexity**
   - Feels verbose for trivial operations
   - But pays off as system grows
   - Complexity hidden by architecture

## Rationale

### Why CQRS?

- Clear separation: Command (write) vs Query (read later)
- Enables independent optimization
- Explicit about side effects
- Future: separate read model consistency

### Why Event Sourcing?

- Complete audit trail
- Time travel capability
- Replay for debugging
- Immutable source of truth
- Enables complex domain logic

### Why Ports/Adapters?

- Application = pure business orchestration
- Infrastructure = technical implementation
- Swappable parts
- Testable in isolation

### Why DTOs?

- External contracts different from domain
- Decouples API evolution from domain
- Clear transformation boundaries
- Reusable across channels

## Implementation Details

### CheckInPatientCommand

- Immutable record type
- Contains all parameter data
- No validation (happens at boundary)
- No behavior, only data

### CheckInPatientCommandHandler

- Stateless service
- Orchestrates 5 steps: Load → Execute → Validate → Persist → Publish
- Error handling at each step
- Idempotency via correlation ID

### IEventStore Port

```csharp
Task<WaitingQueue?> LoadAsync(string aggregateId)
Task SaveAsync(WaitingQueue aggregate)
Task<IEnumerable<DomainEvent>> GetEventsAsync(string aggregateId)
```

### IEventPublisher Port

```csharp
Task PublishAsync(IEnumerable<DomainEvent> events)
```

## Testing Strategy

**Unit Tests:**

```csharp
[Fact]
public async Task HandleAsync_ValidCommand_SavesAndPublishesEvents()
{
    // Arrange: create aggregate, setup mocks
    var queue = WaitingQueue.Create(queueId, "Main", 10, metadata);
    var eventStoreMock = new Mock<IEventStore>();
    var publisherMock = new Mock<IEventPublisher>();

    eventStoreMock
        .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(queue);

    var handler = new CheckInPatientCommandHandler(
        eventStoreMock.Object,
        publisherMock.Object);

    // Act
    await handler.HandleAsync(command);

    // Assert: verify orchestration correctness
    eventStoreMock.Verify(es => es.SaveAsync(...), Times.Once);
    publisherMock.Verify(pub => pub.PublishAsync(...), Times.Once);
}
```

## Alternatives Considered

### 1. Service Layer with Business Logic

```
// ❌ Anti-pattern
public class CheckInService
{
    public void CheckInPatient(string queueId, string patientId)
    {
        if (queue.Capacity < CurrentCount) // ❌ Business rule in service
            throw new Exception();
        context.SaveChanges(); // ❌ Direct DB access
    }
}
```

**Rejected:** Violates DDD, mixes concerns, tightly coupled to infrastructure.

### 2. Anemic Domain

```
// ❌ No behaviors
public class Patient
{
    public string Id { get; set; }
    public string Name { get; set; }
    // No methods, no logic
}
```

**Rejected:** Domain forces all logic to services, loses cohesion.

### 3. Thick Commands

```
// ❌ Too much logic
public class CheckInPatientCommand
{
    public async Task Execute() // ❌ Command is handler
    {
        // 50 lines of logic here
    }
}
```

**Rejected:** Commands become commands+handlers, loses separation.

## Acceptance Criteria

✅ Application layer contains zero domain logic
✅ Application layer contains zero infrastructure code
✅ Tests run without mocks (but use them)
✅ Handlers follow consistent pattern
✅ Ports separate application from infrastructure
✅ DTOs used at boundaries
✅ Commands immutable records
✅ Handlers stateless services

## Operational Alignment (2026-02-20)

- Application orchestration is aligned with role-separated commands: reception, cashier, and medical.
- Handlers now orchestrate additional command flows for payment pending/validation, cashier absence, cancellation by payment, and consultation absence.
- Medical claim-next flow enforces active consulting-room validation before queue selection.
- This ADR keeps the same architectural decision while reflecting the current operational workflow in runtime APIs.

## References

- **Domain-Driven Design** (Eric Evans) - Bounded Contexts, Aggregates
- **CQRS Pattern** - Command Query Responsibility Segregation
- **Event Sourcing** - Martin Fowler
- **Hexagonal Architecture** - Alistair Cockburn (Ports & Adapters)

## Related ADRs

- **ADR-004:** Event Sourcing as Primary Persistence Strategy
- **ADR-005:** CQRS (Command Query Responsibility Segregation)
- **ADR-007:** Hexagonal Architecture (Ports & Adapters)
- **ADR-010:** Attention Workflow State Machine

---

**Approval:** ✅ ACCEPTED
**Phase:** 2 - Application Layer
**Next Phase:** 3 - Infrastructure Layer Implementation
