# ADR-007: Hexagonal Architecture (Ports & Adapters)

**Date:** 2026-02-19
**Status:** ACCEPTED
**Context:** Architectural pattern for domain isolation and testability
**Decision Makers:** Enterprise Architect Team

---

## Context

### Problem

Traditional layered architectures couple business logic to infrastructure:

```
┌─────────────────────────────────────┐
│          Presentation               │
├─────────────────────────────────────┤
│          Business Logic             │  ← Depends on ↓
├─────────────────────────────────────┤
│   Data Access (SQL, EF, Dapper)    │  ← Framework coupled
└─────────────────────────────────────┘
```

**Problems:**

- ❌ Business logic depends on infrastructure
- ❌ Hard to test (requires real DB, external services)
- ❌ Technology changes ripple through domain
- ❌ Framework lock-in (EntityFramework, Dapper, etc.)
- ❌ Database-driven design (tables → entities)
- ❌ Business rules scattered across layers

**Real scenario:**

```csharp
// ❌ BAD: Business logic depends on infrastructure
public class WaitingQueueService
{
    private readonly DbContext _context;  // Infrastructure leaking in

    public async Task CheckInPatient(CheckInRequest request)
    {
        var queue = await _context.WaitingQueues.FindAsync(request.QueueId);
        // Business logic mixed with EF specifics
    }
}
```

This violates:

- Dependency Inversion Principle
- Clean Architecture
- Testability

---

## Decision

**Implement Hexagonal Architecture to isolate domain from infrastructure.**

### Architecture

```
                    ┌──────────────────────────────┐
                    │        ADAPTERS              │
                    │   (Infrastructure)           │
                    │                              │
    HTTP ───────────┤→ API Controllers             │
    gRPC ───────────┤→ gRPC Services               │
                    │                              │
                    └──────────┬───────────────────┘
                               │
                               ↓ Implements Ports
                    ┌──────────────────────────────┐
                    │          PORTS               │
                    │      (Interfaces)            │
                    │  IEventStore                 │
                    │  IEventPublisher             │
                    │  IClock                      │
                    └──────────┬───────────────────┘
                               │
                               ↓ Used by
                    ┌──────────────────────────────┐
                    │         DOMAIN               │
                    │     (Business Logic)         │
                    │                              │
                    │  ← NO DEPENDENCIES           │
                    │                              │
                    │  Aggregates + Rules          │
                    │  Domain Events               │
                    │  Value Objects               │
                    └──────────────────────────────┘
```

### Layers

#### 1. Domain (Core)

**Responsibility:** Business logic, domain entities, business rules

```csharp
// Domain/Aggregates/WaitingQueue.cs
public sealed class WaitingQueue : AggregateRoot
{
    // Pure business logic
    // NO infrastructure dependencies
    // NO frameworks

    public void CheckInPatient(CheckInPatientRequest request)
    {
        // Business rule validation
        if (_patients.Count >= _maxCapacity)
            throw new DomainException("Queue is full");

        var patient = WaitingPatient.Create(/* ... */);
        Apply(new PatientCheckedIn(/* ... */));
    }
}
```

**Dependencies:** None (or only BuildingBlocks)

#### 2. Application (Orchestration)

**Responsibility:** Use cases, orchestrate domain and ports

```csharp
// Application/CommandHandlers/CheckInPatientCommandHandler.cs
public sealed class CheckInPatientCommandHandler
{
    private readonly IEventStore _eventStore;       // ← PORT
    private readonly IEventPublisher _eventPublisher; // ← PORT
    private readonly IClock _clock;                 // ← PORT

    public async Task<int> HandleAsync(CheckInPatientCommand command)
    {
        // 1. Load aggregate (via port)
        var queue = await _eventStore.LoadAsync(command.QueueId);

        // 2. Execute business logic (pure domain)
        queue.CheckInPatient(request);

        // 3. Save (via port)
        await _eventStore.SaveAsync(queue);

        // 4. Publish (via port)
        await _eventPublisher.PublishAsync(queue.UncommittedEvents);

        return queue.UncommittedEvents.Count;
    }
}
```

**Dependencies:** Domain + Ports (interfaces only)

#### 3. Ports (Contracts)

**Responsibility:** Define contracts for infrastructure

```csharp
// Application/Ports/IEventStore.cs
public interface IEventStore
{
    Task<TAggregate> LoadAsync<TAggregate>(string aggregateId)
        where TAggregate : AggregateRoot, new();

    Task SaveAsync<TAggregate>(TAggregate aggregate)
        where TAggregate : AggregateRoot;
}

// Application/Ports/IEventPublisher.cs
public interface IEventPublisher
{
    Task PublishAsync(IEnumerable<DomainEvent> events);
}

// Application/Ports/IClock.cs
public interface IClock
{
    DateTime UtcNow { get; }
}
```

**Dependencies:** None (pure contracts)

#### 4. Infrastructure (Adapters)

**Responsibility:** Implement ports with concrete technology

```csharp
// Infrastructure/Persistence/EventStore/PostgresEventStore.cs
public sealed class PostgresEventStore : IEventStore  // ← Implements PORT
{
    private readonly string _connectionString;
    private readonly IEventSerializer _serializer;

    public async Task SaveAsync<TAggregate>(TAggregate aggregate)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        // PostgreSQL-specific implementation
        // Npgsql, Dapper, SQL queries
    }
}

// Infrastructure/Messaging/RabbitMqEventPublisher.cs
public sealed class RabbitMqEventPublisher : IEventPublisher  // ← Implements PORT
{
    private readonly IConnection _connection;

    public async Task PublishAsync(IEnumerable<DomainEvent> events)
    {
        // RabbitMQ-specific implementation
    }
}

// Infrastructure/Clock/SystemClock.cs
public sealed class SystemClock : IClock  // ← Implements PORT
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

**Dependencies:** External libraries (Npgsql, RabbitMQ.Client)

---

## Consequences

### Positive ✅

1. **Domain Isolation**
   - Business logic has zero infrastructure dependencies
   - Domain doesn't know about PostgreSQL, RabbitMQ, HTTP
   - Pure C# domain model

2. **Testability**
   - Domain: Pure unit tests (no mocks needed)
   - Application: Test with fake adapters
   - No database required for 90% of tests

3. **Technology Independence**
   - Switch PostgreSQL → MongoDB: Change adapter only
   - Switch RabbitMQ → Kafka: Change adapter only
   - Domain remains unchanged

4. **Dependency Inversion**
   - Infrastructure depends on Application (ports)
   - Application depends on Domain
   - Domain depends on nothing

5. **Parallel Development**
   - Team A: Domain + Application
   - Team B: Infrastructure adapters
   - No blocking on external services

6. **Clearer Responsibilities**
   - Domain: "What" (business rules)
   - Application: "How" (orchestration)
   - Infrastructure: "With what" (implementation)

### Negative ❌

1. **More Abstractions**
   - Interfaces for every dependency
   - Indirection increases
   - More files to navigate

2. **Initial Setup Cost**
   - Requires upfront architectural design
   - More boilerplate than "new DbContext()"
   - Learning curve for team

3. **Over-abstraction Risk**
   - Easy to create unnecessary ports
   - Premature abstraction
   - YAGNI violation

### Mitigations

| Risk | Mitigation |
|------|-----------|
| **Too many interfaces** | Only create ports for external dependencies |
| **Complexity** | Clear documentation, onboarding guide |
| **YAGNI** | Start with concrete, extract ports when needed |

---

## Alternatives Considered

### 1. Traditional N-Layer Architecture

```
UI → Business Logic → Data Access → Database
```

**Pros:**

- ✅ Simple
- ✅ Familiar to most developers
- ✅ Less code

**Cons:**

- ❌ Business logic depends on infrastructure
- ❌ Hard to test
- ❌ Framework lock-in

**Rejected:** Violates DIP, poor testability.

### 2. Clean Architecture (Uncle Bob)

**Pros:**

- ✅ Similar to Hexagonal
- ✅ Domain isolation
- ✅ Testability

**Cons:**

- ❌ More layers (Entities, Use Cases, Interface Adapters, Frameworks)
- ❌ More complex

**Considered:** Very similar, went with Hexagonal for simplicity.

### 3. Onion Architecture

**Pros:**

- ✅ Domain at center
- ✅ Dependency inversion

**Cons:**

- ❌ Similar to Hexagonal
- ❌ Less community adoption

**Considered:** Hexagonal more established name.

### 4. Transaction Script (CRUD)

**Pros:**

- ✅ Extremely simple
- ✅ Fast to implement

**Cons:**

- ❌ No domain model
- ❌ Business logic scattered
- ❌ Not scalable

**Rejected:** Not suitable for complex domains.

---

## Tradeoffs

| Aspect | Layered | Hexagonal | Winner |
|--------|---------|-----------|--------|
| **Simplicity** | High | Medium | Layered |
| **Testability** | Low | High | Hexagonal ✅ |
| **Tech independence** | Low | High | Hexagonal ✅ |
| **DIP compliance** | No | Yes | Hexagonal ✅ |
| **Setup time** | Fast | Slow | Layered |
| **Long-term maintainability** | Low | High | Hexagonal ✅ |

**Decision:** Long-term benefits outweigh initial setup cost.

---

## Implementation Status

### Completed ✅

#### Domain Layer

- ✅ `WaitingQueue` aggregate
- ✅ `WaitingPatient` entity
- ✅ Value objects (QueueId, PatientId, Priority)
- ✅ Domain events
- ✅ Business rules (capacity, uniqueness)

#### Application Layer

- ✅ Command handlers
- ✅ Ports (IEventStore, IEventPublisher, IClock)
- ✅ No infrastructure dependencies

#### Infrastructure Layer

- ✅ `PostgresEventStore` adapter
- ✅ `RabbitMqEventPublisher` adapter
- ✅ `SystemClock` adapter
- ✅ Dependency injection setup

### In Progress 🚧

- 🚧 Additional adapters (logging, monitoring)

### Future 📅

- 📅 gRPC adapter (alternative to REST)
- 📅 MongoDB adapter (alternative persistence)
- 📅 Kafka adapter (alternative messaging)

---

## Validation

### Dependency Rules Validation

```bash
# Domain should have NO dependencies
dotnet list WaitingRoom.Domain/WaitingRoom.Domain.csproj package
# Result: Only BuildingBlocks (allowed)

# Application should NOT depend on Infrastructure
dotnet list WaitingRoom.Application/WaitingRoom.Application.csproj package
# Result: Only Domain + BuildingBlocks

# Infrastructure should implement ports
# Check: PostgresEventStore implements IEventStore ✅
# Check: RabbitMqEventPublisher implements IEventPublisher ✅
```

### Test Coverage

| Layer | Unit Tests | Dependencies |
|-------|------------|--------------|
| **Domain** | 49 | Zero mocks ✅ |
| **Application** | 7 | Fake adapters ✅ |
| **Infrastructure** | 0 | Real services |
| **Integration** | 4 | Full stack |

---

## Design Guidelines

### Creating a Port

**When to create:**

- ✅ External system (DB, message broker, cache)
- ✅ Non-deterministic behavior (clock, random)
- ✅ Infrastructure concern (logging, monitoring)

**When NOT to create:**

- ❌ Internal domain services
- ❌ Pure functions
- ❌ Value objects

**Example:**

```csharp
// ✅ GOOD: External system
public interface IEventStore { ... }

// ❌ BAD: Internal domain service (no external dependency)
public interface IDomainCalculator { ... }  // Just make it a domain service class
```

### Creating an Adapter

**Rules:**

- ✅ One adapter per port
- ✅ Adapter name describes technology: `PostgresEventStore`, `RabbitMqEventPublisher`
- ✅ Register in DI container
- ✅ Keep adapter thin (delegate to libraries)

### Anti-patterns to Avoid

- ❌ **Domain depending on Application** — Never
- ❌ **Application depending on Infrastructure** — Only via ports
- ❌ **Leaking infrastructure details** — IEventStore should not expose `NpgsqlConnection`
- ❌ **Fat adapters** — Business logic belongs in Domain
- ❌ **Adapter calling adapter directly** — Go through ports

---

## Testing Strategy

### Domain Tests (Pure Unit Tests)

```csharp
[Fact]
public void CheckInPatient_Should_Emit_PatientCheckedIn_Event()
{
    // Arrange
    var queue = WaitingQueue.Create(/* ... */);

    // Act
    queue.CheckInPatient(request);

    // Assert
    Assert.Single(queue.UncommittedEvents);
    Assert.IsType<PatientCheckedIn>(queue.UncommittedEvents.First());
}
// ✅ No mocks, no infrastructure, pure logic
```

### Application Tests (Fake Adapters)

```csharp
[Fact]
public async Task HandleAsync_Should_SaveAndPublish()
{
    // Arrange
    var fakeEventStore = new FakeEventStore();
    var fakePublisher = new FakeEventPublisher();
    var handler = new CheckInPatientCommandHandler(fakeEventStore, fakePublisher, Clock.System);

    // Act
    await handler.HandleAsync(command);

    // Assert
    Assert.Single(fakeEventStore.SavedAggregates);
    Assert.Single(fakePublisher.PublishedEvents);
}
// ✅ In-memory fakes, fast, deterministic
```

### Integration Tests (Real Adapters)

```csharp
[Fact]
public async Task EndToEnd_Should_ProcessEvent()
{
    // Use real PostgresEventStore, real RabbitMQ
    // Validate full pipeline
}
// ✅ Real infrastructure, confidence in production behavior
```

---

## References

- Alistair Cockburn - "Hexagonal Architecture" (original paper)
- Robert C. Martin - "Clean Architecture"
- Eric Evans - "Domain-Driven Design"
- Steve Smith - "Ardalis.CleanArchitecture"

---

## Notes

- Hexagonal Architecture = Ports & Adapters = Clean Architecture (similar ideas)
- "Port" = interface defined by Application
- "Adapter" = concrete implementation in Infrastructure
- Domain is "inside the hexagon" (protected)
- Infrastructure is "outside the hexagon" (replaceable)
- The "hexagon" shape is symbolic (can have any number of ports)

---

**Supersedes:** None
**Superseded by:** None
**Related ADRs:**

- ADR-004: Event Sourcing (IEventStore port)
- ADR-005: CQRS (ports separate read/write)
- ADR-006: Outbox Pattern (IEventPublisher port)
