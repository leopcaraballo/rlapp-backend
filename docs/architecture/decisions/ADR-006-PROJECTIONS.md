# ADR-006: CQRS Read Model with Event-Sourced Projections

**Date:** 2026-02-19
**Status:** ACCEPTED
**Authors:** Architecture Team
**Related:** ADR-002 (Application Layer), ADR-004 (Outbox Worker), ADR-005 (API Layer)

---

## Context

The RLAPP system has implemented Event Sourcing for the write model (WaitingQueue aggregate) and API for command execution.

To enable **fast, denormalized queries** without coupling the query model to domain logic, we need:

1. **CQRS Read Side** — Separate query models optimized for read access
2. **Deterministic Projections** — Idempotent event handlers that build read models from events
3. **Projection State Management** — Track projection progress (checkpoints) for rebuild capability
4. **Query API Endpoints** — Expose read models via HTTP for clients

Key requirements:

- Read Models must be **completely independent** of Write Model implementation
- Projections must be **idempotent** (same event processed twice = same state)
- Must support **deterministic replay** from EventStore for debugging
- Projections must be **rebuildable** from full event history
- Query API must not expose infrastructure implementation details
- **NO business logic** in projections (pure denormalization)

---

## Decision

Implement CQRS Read Model with Event-Sourced Projections following these principles:

### 1. Projection Architecture

```
Domain Events (EventStore)
        ↓
   IProjectionHandler (idempotent)
        ↓
   IProjection (state management)
        ↓
   ProjectionCheckpoint (idempotency tracking)
        ↓
   Read Model / View (denormalized)
        ↓
   Query API Endpoints
```

### 2. Core Abstractions

#### IProjectionHandler

```csharp
namespace WaitingRoom.Projections;

using BuildingBlocks.EventSourcing;

public interface IProjectionHandler
{
    /// Get event type this handler processes
    string EventName { get; }

    /// Process event idempotently
    /// Input: Event + current projection state
    /// Output: Updated projection state
    /// Must be deterministic and idempotent
    Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellation);
}
```

#### IProjection

```csharp
public interface IProjection
{
    /// Unique projection identifier
    string ProjectionId { get; }

    /// Get current checkpoint (for resume after interruption)
    Task<ProjectionCheckpoint?> GetCheckpointAsync(CancellationToken cancellation);

    /// Save checkpoint after processing event
    Task SaveCheckpointAsync(
        ProjectionCheckpoint checkpoint,
        CancellationToken cancellation);

    /// Rebuild projection from scratch
    /// Clears all data and replays all events
    Task RebuildAsync(CancellationToken cancellation);

    /// Get all handlers for this projection
    IReadOnlyList<IProjectionHandler> GetHandlers();
}
```

#### ProjectionCheckpoint

```csharp
public record ProjectionCheckpoint
{
    public required string ProjectionId { get; init; }
    public required long LastEventVersion { get; init; }
    public required DateTimeOffset CheckpointedAt { get; init; }
    public required string IdempotencyKey { get; init; } // Prevents duplicate processing
}
```

### 3. Read Models (Views)

#### WaitingRoomMonitorView

**Purpose:** Dashboard overview of waiting room status

```csharp
public record WaitingRoomMonitorView
{
    public required string QueueId { get; init; }
    public required int TotalPatientsWaiting { get; init; }
    public required int HighPriorityCount { get; init; }
    public required int NormalPriorityCount { get; init; }
    public required int LowPriorityCount { get; init; }
    public required DateTime? LastPatientCheckedInAt { get; init; }
    public required int AverageWaitTimeMinutes { get; init; }
    public required DateTimeOffset ProjectedAt { get; init; }
}
```

#### QueueStateView

**Purpose:** Current state of queue with patient details

```csharp
public record PatientInQueueDto
{
    public required string PatientId { get; init; }
    public required string PatientName { get; init; }
    public required string Priority { get; init; }
    public required DateTime CheckInTime { get; init; }
    public required int WaitTimeMinutes { get; init; }
}

public record QueueStateView
{
    public required string QueueId { get; init; }
    public required int CurrentCapacity { get; init; }
    public required int MaxCapacity { get; init; }
    public required bool IsAtCapacity { get; init; }
    public required List<PatientInQueueDto> PatientsInQueue { get; init; }
    public required DateTimeOffset ProjectedAt { get; init; }
}
```

### 4. Key Design Principles

#### Idempotency Guarantee

```csharp
// Same event processed twice MUST produce same state
public class PatientCheckedInHandler : IProjectionHandler
{
    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellation)
    {
        var patientCheckedIn = (PatientCheckedIn)@event;

        // Use idempotency key to prevent duplicate inserts
        var idempotencyKey = $"{patientCheckedIn.Metadata.AggregateId}:{patientCheckedIn.Metadata.EventId}";

        var existingRecord = await context.FindByIdempotencyKey(idempotencyKey);
        if (existingRecord != null)
            return; // Already processed this event

        // Insert or update queue state
        await context.UpdateQueueStateAsync(patientCheckedIn);
        await context.RecordIdempotencyAsync(idempotencyKey);
    }
}
```

#### Deterministic Replay

- Events always applied in order
- Same event applied to same state = same result
- Checkpoint ensures exactly-once processing
- Rebuild produces identical final state

#### Separation of Concerns

```
✅ Projection Handler: Event interpretation + idempotency logic
✅ Read Model: Denormalized structure optimized for queries
❌ Projection: NO domain rules, NO business logic
❌ Projection: NO direct domain access (only events)
```

### 5. Event Handler Implementation Pattern

All handlers follow this structure:

1. **Extract data** from event
2. **Check idempotency** using IdempotencyKey
3. **Update read model state** deterministically
4. **Record processing** via checkpoint

Example:

```csharp
public sealed class PatientCheckedInProjectionHandler : IProjectionHandler
{
    private readonly IProjectionContext _context;

    public string EventName => nameof(PatientCheckedIn);

    public async Task HandleAsync(
        DomainEvent @event,
        IProjectionContext context,
        CancellationToken cancellation)
    {
        var evt = (PatientCheckedIn)@event;

        // Idempotency check
        var key = GenerateIdempotencyKey(evt);
        if (await context.ProcessedAsync(key, cancellation))
            return;

        // Update views deterministically
        await context.UpdateQueueMonitorAsync(evt, cancellation);
        await context.UpdateQueueStateAsync(evt, cancellation);

        // Mark as processed
        await context.MarkProcessedAsync(key, cancellation);
    }

    private string GenerateIdempotencyKey(PatientCheckedIn evt)
        => $"{evt.Metadata.AggregateId}:{evt.Metadata.EventId}";
}
```

### 6. Projection Rebuild Process

```csharp
public async Task RebuildAsync(CancellationToken cancellation)
{
    // Step 1: Clear all projection data
    await _context.ClearAsync(cancellation);

    // Step 2: Get all events from EventStore (deterministic order)
    var allEvents = await _eventStore.GetAllEventsAsync(cancellation);

    // Step 3: Replay each event through handlers
    foreach (var @event in allEvents)
    {
        var handler = _handlers.FirstOrDefault(h => h.EventName == @event.EventName);
        if (handler != null)
            await handler.HandleAsync(@event, _context, cancellation);
    }

    // Step 4: Update checkpoint to indicate rebuild complete
    await _context.SaveCheckpointAsync(
        new ProjectionCheckpoint
        {
            ProjectionId = ProjectionId,
            LastEventVersion = allEvents.Max(e => e.Metadata.Version),
            CheckpointedAt = DateTimeOffset.UtcNow,
            IdempotencyKey = Guid.NewGuid().ToString()
        },
        cancellation);
}
```

### 7. Query API Endpoints

All query endpoints return read models (views):

```
GET /api/v1/waiting-room/{queueId}/monitor
    → WaitingRoomMonitorView (dashboard overview)

GET /api/v1/waiting-room/{queueId}/queue-state
    → QueueStateView (detailed current state)

GET /api/v1/waiting-room/{queueId}/rebuild
    → 202 Accepted (async projection rebuild)
```

Endpoints use **async workers** to rebuild projections without blocking API.

---

## Consequences

### Positive ✅

1. **Pure CQRS Separation**
   - Write model isolated from read optimization
   - Read models optimized for specific query patterns
   - Independent scaling of reads

2. **Deterministic and Testable**
   - Replay produces identical state
   - Rebuild verifiable
   - No side effects in projections

3. **Idempotent Event Processing**
   - Safe to replay events multiple times
   - Deduplication via IdempotencyKey
   - Handles outbox failures gracefully

4. **Fast Queries**
   - No joins or complex lookups
   - Denormalized structure ready to serve
   - Query latency in milliseconds

5. **Audit and Debugging**
   - Rebuild from scratch for validation
   - Event history is searchable
   - Projection state snapshots available

6. **No Domain Coupling**
   - Projections never call domain
   - Can evolve independently
   - Safe to add new views

### Negative/Tradeoffs ⚠️

1. **Eventual Consistency**
   - Write model and read model briefly out of sync
   - Clients must accept eventual consistency
   - Real-time consistency not possible

2. **Operational Complexity**
   - Must manage projection state and checkpoints
   - Rebuild can take time on large event histories
   - Monitoring needed for projection lag

3. **Additional Storage**
   - Read models duplicate data from events
   - Storage cost for denormalization
   - Cache invalidation patterns needed

4. **Event Schema Evolution**
   - Must handle old and new event versions
   - Projection must be forward/backward compatible
   - Schema versioning discipline required

---

## Rationale

### Why CQRS?

- **Performance**: Denormalized reads don't require joins
- **Scalability**: Read model can be replicated/cached independently
- **Flexibility**: Different query patterns need different structures
- **Separation**: Write and read concerns don't interfere

### Why Idempotency?

- **Resilience**: Network failures retried without duplicate processing
- **Determinism**: Same event always produces same result
- **Auditability**: Complete event history drives all reads
- **Testability**: Replay for validation and debugging

### Why Checkpoints?

- **Resumability**: After crash, restart from checkpoint not from beginning
- **Performance**: Avoid reprocessing millions of old events
- **Monitoring**: Track projection lag vs event production

### Why Projections Rebuild?

- **Validation**: Verify read models match events
- **Migration**: Change read model structure
- **Debugging**: Reproduce state for specific point in time
- **Disaster recovery**: Recover read model from events

---

## Implementation Details

### File Structure

```
src/Services/WaitingRoom/
├── WaitingRoom.Projections/
│   ├── Abstractions/
│   │   ├── IProjection.cs
│   │   ├── IProjectionHandler.cs
│   │   ├── IProjectionContext.cs
│   │   └── ProjectionCheckpoint.cs
│   ├── Handlers/
│   │   └── PatientCheckedInHandler.cs
│   ├── Views/
│   │   ├── WaitingRoomMonitorView.cs
│   │   ├── QueueStateView.cs
│   │   └── PatientInQueueDto.cs
│   ├── Implementations/
│   │   └── ProjectionEngine.cs
│   └── WaitingRoom.Projections.csproj
├── WaitingRoom.API/
│   └── Endpoints/
│       ├── WaitingRoomMonitorEndpoints.cs
│       └── QueueStateEndpoints.cs
└── Tests/
    ├── WaitingRoom.Tests.Projections/
    │   ├── Idempotency/
    │   ├── Replay/
    │   └── RebuildTest.cs
    └── Integration/
        └── ProjectionIntegrationTest.cs
```

### Configuration

```csharp
// In Program.cs
services.AddProjections()
    .AddProjectionHandler<PatientCheckedInHandler>()
    .AddProjectionEngine<WaitingRoomProjectionEngine>();

// Register read model queries
services.AddScoped<IWaitingRoomMonitorQuery, WaitingRoomMonitorQuery>();
services.AddScoped<IQueueStateQuery, QueueStateQuery>();
```

---

## Testing Strategy

### Unit Tests: Idempotency

```csharp
[Fact]
public async Task handler_processes_event_twice_produces_same_state()
{
    var evt = new PatientCheckedIn { ... };
    var context = new InMemoryProjectionContext();

    await handler.HandleAsync(evt, context);
    var firstState = await context.GetState();

    await handler.HandleAsync(evt, context); // Replay
    var secondState = await context.GetState();

    Assert.Equal(firstState, secondState);
}
```

### Unit Tests: Handlers

- Handler processes correct event type
- Handler extracts data correctly
- Handler updates all affected views
- Handler idempotent via key tracking

### Integration Tests: Replay

- Get events from EventStore
- Replay through projection
- Verify read model matches expected state

### Integration Tests: Rebuild

- Call rebuild endpoint
- Verify all events reprocessed
- Verify final state identical to incremental processing

### E2E Tests

- Create command → verify projection updated
- Rebuild projection → verify consistency
- Query API → verify correct view returned

---

## Alternatives Considered

### 1. Query Model with Event Subscribers (No Checkpoints)

```csharp
// ❌ Loses idempotency on crashes
public async Task OnPatientCheckedIn(PatientCheckedIn evt)
{
    await UpdateQueState(evt); // What if process dies here?
}
```

**Rejected:** No idempotency, loses events on failure, not resumable.

### 2. Direct Domain Queries (No Projections)

```csharp
// ❌ Performance issue
public async Task<QueueStateView> GetQueueState(string queueId)
{
    var events = await _eventStore.GetEventsAsync(queueId);
    var aggregate = AggregateRoot.LoadFromHistory(queueId, events);
    return new QueueStateView { /* map from aggregate */ };
}
```

**Rejected:** Replay entire aggregate for each query, O(N) performance, doesn't scale.

### 3. Eventual Consistency with TTL Cache

```csharp
// ❌ Unbounded staleness
var view = cache.Get("queue:state");
if (view == null)
    view = await RebuilAsync(); // Could be minutes old
```

**Rejected:** No guarantee of freshness, unpredictable lag.

### 4. Synchronous Projection Update (In Command Handler)

```csharp
// ❌ Tight coupling
public async Task HandleCheckIn(CheckInCommand cmd)
{
    await _eventStore.SaveAsync(queue);
    await _projection.UpdateAsync(...); // Synchronous update
}
```

**Rejected:** Command handler blocks on projection, failure couples concerns.

---

## Acceptance Criteria

✅ Projection framework is idempotent (same event twice = same state)
✅ Replay from EventStore produces identical state
✅ Rebuild clears and recreates all projections
✅ Duplicate events handled gracefully
✅ Checkpoints track progress
✅ Query API returns denormalized views
✅ No domain logic in projections
✅ No infrastructure leakage in views
✅ 100% idempotency test coverage
✅ Integration tests verify EventStore → Projection → API flow

---

## Related ADRs

- **ADR-002**: Application Layer (CQRS pattern, Command Handlers)
- **ADR-004**: Outbox Worker (Event publication to projections)
- **ADR-005**: API Layer (Query endpoints)

---

## References

- **CQRS Pattern** — Greg Young
- **Event Sourcing** — Martin Fowler
- **Projection Pattern** — DDD Community Patterns
- **Idempotent Consumers** — Apache Kafka Best Practices

---

## Sign-Off

**Architecture Team:** ✅ Approved
**Lead Engineer:** ✅ Ready for implementation
**Phase:** 5 - Projections / Read Models

---

**Next Phase:** Phase 6 - Event-Driven Services & Integration
