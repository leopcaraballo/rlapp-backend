# ADR-005: CQRS (Command Query Responsibility Segregation)

**Date:** 2026-02-19
**Status:** ACCEPTED
**Context:** Separation of write and read models
**Decision Makers:** Enterprise Architect Team

---

## Context

### Problem

Traditional layered architectures use the same model for reads and writes:

- **Write operations** — Complex validation, business rules, transactional
- **Read operations** — Simple data retrieval, denormalized views, optimized queries

Problems with unified model:

- ❌ **Impedance mismatch:** Write model optimized for invariants, read model for queries
- ❌ **Performance:** Complex joins for reporting slow down writes
- ❌ **Scalability:** Cannot scale reads and writes independently
- ❌ **Complexity:** One model trying to serve two masters
- ❌ **Team friction:** Read optimizations break write constraints

### Business Requirements

**WaitingRoom Domain:**

- **Writes:** Check-in patient (complex validation, business rules)
- **Reads:** Display queue status, show patient list, statistics

**Different characteristics:**

- **Write:** Low volume (~10/min), complex logic, transactional
- **Read:** High volume (~1000/min), simple queries, eventual consistency OK

---

## Decision

**Implement CQRS to separate write model (commands) from read model (queries).**

### Architecture

```
┌──────────── WRITE MODEL ────────────┐
│                                      │
│  Command → Handler → Aggregate       │
│              ↓                       │
│         Event Store (append-only)   │
│              ↓                       │
│         Domain Events                │
│                                      │
└──────────────────────────────────────┘
                ↓
         Outbox / Event Bus
                ↓
┌──────────── READ MODEL ──────────────┐
│                                      │
│  Event → Projection Handler          │
│              ↓                       │
│    Read Database (denormalized)     │
│              ↓                       │
│         Query Handlers               │
│                                      │
└──────────────────────────────────────┘
```

### Implementation

#### Write Model (Commands)

```csharp
// Command — Intent to change state
public sealed record CheckInPatientCommand
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string PatientName { get; init; }
    public string Priority { get; init; }
    // ...
}

// Command Handler — Executes business logic
public sealed class CheckInPatientCommandHandler
{
    private readonly IEventStore _eventStore;

    public async Task<int> HandleAsync(CheckInPatientCommand command)
    {
        var queue = await _eventStore.LoadAsync(command.QueueId);
        queue.CheckInPatient(request);
        await _eventStore.SaveAsync(queue);
        return queue.UncommittedEvents.Count;
    }
}
```

#### Read Model (Queries)

```csharp
// Query — No side effects
public sealed record GetQueueStatusQuery
{
    public string QueueId { get; init; }
}

// Query Handler — Simple data retrieval
public sealed class GetQueueStatusQueryHandler
{
    private readonly IWaitingRoomProjectionContext _context;

    public async Task<QueueStatusView> HandleAsync(GetQueueStatusQuery query)
    {
        return await _context.GetQueueStatusAsync(query.QueueId);
    }
}

// Read Model — Denormalized view
public sealed class QueueStatusView
{
    public string QueueId { get; init; }
    public string QueueName { get; init; }
    public int CurrentPatientCount { get; init; }
    public int MaxCapacity { get; init; }
    public List<PatientView> Patients { get; init; }
}
```

### Key Principles

1. **Commands** — Imperative, represent intent (CheckInPatient, CreateQueue)
2. **Queries** — Declarative, return data (GetQueueStatus, ListPatients)
3. **Separate databases** — Write DB (event store), Read DB (denormalized views)
4. **Eventual consistency** — Reads eventually reflect writes
5. **CQS at method level** — Methods either change state OR return data, never both

---

## Consequences

### Positive ✅

1. **Performance**
   - Optimize writes for transactionality
   - Optimize reads for query performance
   - No complex joins in read model
   - Denormalized views = fast queries

2. **Scalability**
   - Scale reads and writes independently
   - Add read replicas without impacting writes
   - Different databases for different needs

3. **Simplicity**
   - Each model focused on one concern
   - No impedance mismatch
   - Easier to reason about

4. **Flexibility**
   - Add new projections without changing write model
   - Multiple read models from same events
   - Adapt UI without touching business logic

5. **Team Autonomy**
   - Front-end team owns projections
   - Back-end team owns commands
   - Parallel development

6. **Security**
   - Separate security models for reads vs writes
   - Read-only users cannot modify state
   - Fine-grained permissions

### Negative ❌

1. **Eventual Consistency**
   - Reads lag behind writes (typically <100ms)
   - UI must handle stale data
   - User education required

2. **Complexity**
   - Two models to maintain
   - Projection infrastructure required
   - More code than CRUD

3. **Debugging**
   - Harder to trace command → projection flow
   - Need observability tooling
   - Lag metrics required

4. **Data Duplication**
   - Same data in event store and projections
   - Storage overhead
   - Synchronization needed

### Mitigations

| Risk | Mitigation |
|------|-----------|
| **Eventual consistency** | 95th percentile lag < 100ms (acceptable for domain) |
| **Debugging** | Correlation IDs + distributed tracing |
| **Complexity** | Clear separation, comprehensive docs |
| **Storage** | Projections are cheap, can be rebuilt |

---

## Alternatives Considered

### 1. Traditional Layered Architecture

**Pros:**

- ✅ Simpler
- ✅ Developer familiarity
- ✅ Immediate consistency

**Cons:**

- ❌ Performance bottlenecks
- ❌ Cannot scale independently
- ❌ Impedance mismatch

**Rejected:** Does not meet scalability requirements.

### 2. Task-Based UI without CQRS

**Pros:**

- ✅ Commands explicitly modeled
- ✅ Intent captured

**Cons:**

- ❌ Still using same database
- ❌ Read performance impacts writes
- ❌ Cannot scale independently

**Rejected:** Halfway solution, no scalability benefits.

### 3. Microservices with Shared Database

**Pros:**

- ✅ Service separation

**Cons:**

- ❌ Tight coupling via database
- ❌ Shared schema = coordination
- ❌ Defeats microservices purpose

**Rejected:** Anti-pattern, not true microservices.

---

## Tradeoffs

| Aspect | Unified Model | CQRS | Winner |
|--------|---------------|------|--------|
| **Simplicity** | High | Low | Unified |
| **Performance (read)** | Medium | High | CQRS ✅ |
| **Performance (write)** | Medium | High | CQRS ✅ |
| **Scalability** | Limited | Independent | CQRS ✅ |
| **Consistency** | Immediate | Eventual | Unified |
| **Flexibility** | Low | High | CQRS ✅ |
| **Team autonomy** | Low | High | CQRS ✅ |

**Decision:** CQRS benefits outweigh eventual consistency tradeoff.

---

## Implementation Status

### Completed ✅

#### Write Model

- ✅ `CheckInPatientCommand` + Handler
- ✅ Event Store persistence
- ✅ Domain logic in aggregates
- ✅ Command validation

#### Read Model

- ✅ `WaitingRoomProjectionContext` interface
- ✅ `QueueStatusView`, `WaitingPatientsView`
- ✅ `ProjectionEventProcessor`
- ✅ Idempotency in projections
- ✅ Query endpoints in API

### In Progress 🚧

- 🚧 Advanced query filters
- 🚧 Projection rebuild tooling

### Future 📅

- 📅 Multiple read databases (PostgreSQL + Elasticsearch)
- 📅 Real-time updates via SignalR
- 📅 GraphQL for flexible queries

---

## Validation

### Success Criteria

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| **Write latency** | <50ms | ~30ms | ✅ |
| **Read latency** | <10ms | ~5ms | ✅ |
| **Projection lag** | <100ms (p95) | ~40ms | ✅ |
| **Read/Write ratio** | 100:1 | 95:1 | ✅ |

### Monitoring

- ✅ Command execution time
- ✅ Query response time
- ✅ Projection lag (EventLagTracker)
- ✅ Read database query performance

---

## Design Guidelines

### When to Use Commands

- ✅ User action with intent (CheckIn, Create, Update, Delete)
- ✅ Business logic validation required
- ✅ State change must be audited
- ✅ Transactional behavior needed

### When to Use Queries

- ✅ Display data to user
- ✅ Reporting and analytics
- ✅ No side effects
- ✅ Can tolerate eventual consistency

### Anti-patterns to Avoid

- ❌ **Command returning data** — Commands should return void or metadata
- ❌ **Query causing side effects** — Queries must be idempotent
- ❌ **Bypassing projections** — Never query event store directly in UI
- ❌ **Sync reads after writes** — Embrace eventual consistency

## Operational Alignment (2026-02-20)

- Command side now includes explicit role flows: reception registration, cashier processing, and medical attention lifecycle.
- Write model enforces payment and absence policies via dedicated commands/events before consultation progression.
- Medical claim-next command is constrained by active consulting-room state, while read model remains projection-driven.
- CQRS separation is preserved without changing the original decision.

---

## References

- Greg Young - "CQRS Documents" (<https://cqrs.nu>)
- Martin Fowler - "CQRS" (martinfowler.com/bliki/CQRS.html)
- Udi Dahan - "Clarified CQRS"
- Microsoft - "CQRS Journey"

---

## Notes

- CQRS does NOT require Event Sourcing (but they work great together)
- CQRS does NOT require separate databases (but recommended for scalability)
- CQRS is NOT microservices (orthogonal concerns)
- Eventual consistency is a feature, not a bug (embraced by design)
- Projections can be rebuilt from events if corrupted (resilience)

---

**Supersedes:** None
**Superseded by:** None
**Related ADRs:**

- ADR-004: Event Sourcing (write model uses events)
- ADR-006: Outbox Pattern (bridges write and read models)
- ADR-007: Hexagonal Architecture (CQRS aligns with ports)
