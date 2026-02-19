# Application Layer — RLAPP WaitingRoom Service

## Overview

The Application Layer is the **orchestration hub** of the WaitingRoom service. It implements the CQRS pattern combined with Event Sourcing.

**Key Principle:** Application does NOT contain business logic. It orchestrates domain operations.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    External World                        │
│            (Controllers, Events, Queries)               │
└────────────────┬────────────────────────────────────────┘
                 │
        ┌────────▼────────┐
        │  Application    │
        │  (Orchestrator) │
        ├─────────────────┤
        │ • Commands      │
        │ • Handlers      │
        │ • Ports         │
        │ • DTOs          │
        └────────┬────────┘
                 │
        ┌────────▼────────┐
        │     Domain      │
        │  (Pure Logic)   │
        └────────┬────────┘
                 │
        ┌────────▼────────────────┐
        │  Infrastructure          │
        │  • EventStore           │
        │  • EventPublisher       │
        │  • Projections          │
        └─────────────────────────┘
```

## Components

### 1. Commands

**Purpose:** Represent user intents/requests

**Files:**

- `Commands/CheckInPatientCommand.cs`

**Characteristics:**

- Immutable (C# `record` type)
- Contains all data needed for operation
- Named as imperative verb (CheckIn...)
- No validation logic

**Example:**

```csharp
var command = new CheckInPatientCommand
{
    QueueId = "QUEUE-01",
    PatientId = "PAT-001",
    PatientName = "John Doe",
    Priority = Priority.High,
    ConsultationType = "General",
    Actor = "nurse-001"
};
```

### 2. Command Handlers

**Purpose:** Orchestrate the command execution

**Files:**

- `CommandHandlers/CheckInPatientCommandHandler.cs`

**Responsibilities:**

1. Load aggregate from EventStore (reconstruct from history)
2. Execute domain logic via aggregate method
3. Handle domain exceptions
4. Persist events atomically
5. Publish events for subscribers

**Pattern:**

```
Load → Execute Domain → Validate → Persist → Publish
```

**Key Aspects:**

- Handlers are stateless
- All business rules enforced by domain
- Handlers focus on orchestration only
- Errors from domain bubble up

### 3. Ports (Interfaces)

**Purpose:** Define contracts between Application and Infrastructure

**Files:**

- `Ports/IEventStore.cs`
- `Ports/IEventPublisher.cs`

**Design Principle:** Dependency Inversion

- Application depends on abstractions (ports)
- Infrastructure implements ports
- Swappable implementations without changing Application

**Key Ports:**

#### IEventStore

```csharp
// Load aggregate from event history
Task<WaitingQueue?> LoadAsync(string aggregateId)

// Persist uncommitted events
Task SaveAsync(WaitingQueue aggregate)

// Get all events for aggregate
Task<IEnumerable<DomainEvent>> GetEventsAsync(string aggregateId)
```

#### IEventPublisher

```csharp
// Publish events to subscribers
Task PublishAsync(IEnumerable<DomainEvent> events)
```

### 4. DTOs (Data Transfer Objects)

**Purpose:** Transfer data across boundaries

**Files:**

- `DTOs/CheckInPatientDto.cs`
- `DTOs/WaitingQueueDto.cs`
- `DTOs/PatientInQueueDto.cs`

**Characteristics:**

- Immutable records
- No domain logic
- Match external contracts (API, messages)
- Mapping happens at boundaries (Controllers)

**Example:**

```csharp
// From API
var dto = new CheckInPatientDto
{
    QueueId = request.QueueId,
    PatientId = request.PatientId,
    PatientName = request.PatientName,
    // ...
};

// Convert to command
var command = new CheckInPatientCommand { ... };
```

### 5. Exceptions

**Purpose:** Application-level errors

**Files:**

- `Exceptions/ApplicationException.cs`

**Types:**

- `ApplicationException` - Base exception
- `AggregateNotFoundException` - Aggregate not found
- `EventConflictException` - Concurrent modification

## Command Execution Flow

### Step 1: Command Reception

```csharp
// Controller receives HTTP request
var dto = new CheckInPatientDto { ... };
var command = new CheckInPatientCommand { ... };
```

### Step 2: Handler Invocation

```csharp
var handler = new CheckInPatientCommandHandler(eventStore, publisher);
int eventCount = await handler.HandleAsync(command);
```

### Step 3: Aggregate Loading

```csharp
// Event Store reconstructs aggregate from event history
var queue = await eventStore.LoadAsync(command.QueueId);
```

### Step 4: Domain Execution

```csharp
// Domain enforces all business rules
queue.CheckInPatient(
    patientId,
    patientName,
    priority,
    consultationType,
    metadata
);
// → Generates PatientCheckedIn event if valid
// → Throws DomainException if invalid
```

### Step 5: Persistence

```csharp
// Save uncommitted events atomically
await eventStore.SaveAsync(queue);
```

### Step 6: Publishing

```csharp
// Publish events to subscribers
await publisher.PublishAsync(queue.UncommittedEvents);
```

## Critical Guarantees

### 1. Consistency

- All events from single command saved atomically
- Version conflict detection prevents lost updates
- Replay from event store always produces consistent state

### 2. Idempotency

- Commands must be idempotent
- IdempotencyKey in event metadata enables deduplication
- Duplicate commands produce same result

### 3. Traceability

- CorrelationId threads command → events → projections
- Each event contains causation chain
- Actor field identifies who triggered change

### 4. Pure Orchestration

```
✅ ALLOWED                    ❌ NOT ALLOWED
─────────────────────────────────────────────
Load aggregate              Domain logic
Execute domain method       Validation rules
Persist events              If/else decisions
Publish events              Database access
Error handling             Framework knowledge
```

## Separation of Concerns

### Domain Layer Responsibility

```csharp
// Domain OWNS these
public void CheckInPatient(
    PatientId patientId,
    string patientName,
    Priority priority,
    ConsultationType consultationType,
    DateTime checkedInAt,
    EventMetadata metadata,
    string? notes = null)
{
    // Invariant checks
    if (CurrentCount >= MaxCapacity)
        throw new DomainException("Queue at capacity");

    if (IsPatientAlreadyCheckedIn(patientId))
        throw new DomainException("Patient already in queue");

    // Event generation
    var @event = new PatientCheckedIn { ... };
    RaiseEvent(@event);
}
```

### Application Layer Responsibility

```csharp
// Application ONLY orchestrates
public async Task<int> HandleAsync(
    CheckInPatientCommand command,
    CancellationToken cancellationToken)
{
    // 1. Load
    var queue = await _eventStore.LoadAsync(command.QueueId)
        ?? throw new AggregateNotFoundException(command.QueueId);

    // 2. Delegate to domain
    queue.CheckInPatient(...);

    // 3. Persist
    await _eventStore.SaveAsync(queue, cancellationToken);

    // 4. Publish
    await _eventPublisher.PublishAsync(
        queue.UncommittedEvents,
        cancellationToken);

    return queue.UncommittedEvents.Count;
}
```

## Testing Strategy

### Unit Tests

**Location:** `Tests/WaitingRoom.Tests.Application/`

**Approach:**

- Mock IEventStore and IEventPublisher
- Test handler orchestration logic
- Verify correct order of operations
- Test error scenarios

**Example:**

```csharp
[Fact]
public async Task HandleAsync_ValidCommand_SavesAndPublishesEvents()
{
    // 1. Setup aggregates and mocks
    var queue = WaitingQueue.Create(queueId, "Main", 10, metadata);
    var eventStoreMock = new Mock<IEventStore>();

    // 2. Configure mock behavior
    eventStoreMock
        .Setup(es => es.LoadAsync(queueId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(queue);

    // 3. Execute handler
    var handler = new CheckInPatientCommandHandler(
        eventStoreMock.Object,
        publisherMock.Object);

    var result = await handler.HandleAsync(command);

    // 4. Verify orchestration
    eventStoreMock.Verify(es => es.SaveAsync(...), Times.Once);
    publisherMock.Verify(pub => pub.PublishAsync(...), Times.Once);
}
```

### Integration Tests

**Location:** `Tests/Integration/` (Phase 3)

**Approach:**

- Real EventStore implementation
- Real EventPublisher implementation
- Full command execution flow

## Application Layer Ports and Adapters

```
┌──────────────────────────────────────┐
│      Presentation (API)              │
│    (Next Phase - API Layer)          │
└──────────────────┬───────────────────┘
                   │
        ┌──────────▼──────────┐
        │   Application       │
        │   (This Layer)      │
        ├─────────────────────┤
        │ - Commands          │
        │ - Handlers          │
        │ - DTOs              │
        │ - Ports (Abstract)  │
        └──────────┬──────────┘
                   │
      ┌────────────┴──────────────┐
      │                           │
  (Port)                      (Port)
 IEventStore              IEventPublisher
      │                           │
  ┌───▼──────────────┐  ┌────────▼──────┐
  │  Infrastructure  │  │ Infrastructure │
  │  (Phase 3)       │  │ (Phase 3)      │
  └──────────────────┘  └────────────────┘
```

## Design Decisions Rationale

### 1. Command Pattern Over Direct Method Calls

**Why:**

- Commands are serializable (audit, replay)
- Decouples API from domain
- Enables command sourcing
- Clear request/response semantics

### 2. Handlers as Services

**Why:**

- Explicit orchestration
- Easy to mock/test
- Dependency injection friendly
- Supports middleware/aspects (Phase 3)

### 3. Ports for Infrastructure

**Why:**

- Application independent of persistence
- Enables different implementations (SQL, Document, Event Stream)
- Testability via mocks
- Future-proof architecture

### 4. DTOs Separate from Domain

**Why:**

- Domain objects immutable and pure
- API contracts might differ from domain
- Data transfer different from business objects
- Clear boundary between external/internal

## No-Go Items

❌ **Never put this in Application:**

- SQL queries
- HTTP calls
- Authentication/authorization
- Logging directives
- Cache management
- Validation business rules
- ORM usage
- Framework-specific code

All of these belong in Infrastructure or Adapters.

## Relationship to Other Layers

### With Domain Layer

- **Application calls Domain methods**
- **Domain throws exceptions, Application catches**
- **Domain generates events, Application publishes**

### With Infrastructure Layer (Phase 3)

- **Application depends on ports (interfaces)**
- **Infrastructure implements ports**
- **Swappable implementations**

### With Presentation Layer (Phase 4)

- **Controllers call handlers**
- **DTOs for data transfer**
- **Commands bridge external/internal**

## Next Phase

When Infrastructure Layer is implemented:

- IEventStore.LoadAsync → SQL/Document DB
- IEventStore.SaveAsync → Transactional persistence with Outbox pattern
- IEventPublisher.PublishAsync → Message broker (RabbitMQ, Azure Service Bus, etc.)

---

**Status:** ✅ Phase 2 Complete - Application Layer fully implemented
**Next:** Phase 3 - Infrastructure Layer (EventStore, Projections, Messaging)
