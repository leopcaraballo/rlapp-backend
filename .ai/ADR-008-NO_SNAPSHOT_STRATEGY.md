# ADR-008: No Snapshot Strategy

**Date:** 2026-02-19
**Status:** ACCEPTED
**Context:** Event Sourcing performance optimization decision
**Decision Makers:** Enterprise Architect Team

---

## Context

### Problem

Event Sourcing systems rebuild aggregate state by replaying all events:

```
┌─────────────────────────────────────────┐
│  Load Aggregate (1000 events)          │
│                                         │
│  Event 1 → Apply                        │
│  Event 2 → Apply                        │
│  Event 3 → Apply                        │
│  ...                                    │
│  Event 1000 → Apply                     │
│                                         │
│  = Current State                        │
└─────────────────────────────────────────┘
```

**Performance concern:**

- ❓ What if aggregate has 10,000 events?
- ❓ What if loading takes 5 seconds?
- ❓ Should we cache state with snapshots?

### Snapshot Pattern (Traditional Solution)

```
┌────────────────────────────────────────┐
│  Every 100 events, save snapshot:      │
│                                        │
│  Event 1-100 → Snapshot A             │
│  Event 101-200 → Snapshot B           │
│  Event 201-300 → Snapshot C           │
│                                        │
│  Load: Snapshot C + Events 301-350    │
│  = Fast reconstruction (50 events)    │
└────────────────────────────────────────┘
```

**Pros:**

- ✅ Fast aggregate loading (O(n) → O(k) where k = events since snapshot)
- ✅ Reduces event replay

**Cons:**

- ❌ Added complexity
- ❌ Snapshot storage
- ❌ Snapshot versioning (code changes = incompatible snapshots)
- ❌ Cache invalidation problems
- ❌ Debugging harder (state hidden in snapshot)

### Current System Characteristics

**WaitingRoom Domain:**

- **Aggregate:** WaitingQueue
- **Expected event volume:** 10-50 events per queue per day
- **Max lifetime:** Queue deleted after 24 hours
- **Typical events:** ~200 events max per aggregate

**Performance measurement:**

```
Replay 200 events: ~15ms
Replay 1000 events: ~60ms
Replay 10000 events: ~500ms (never happens in our domain)
```

---

## Decision

**DO NOT implement snapshots. Rely on event replay for aggregate reconstruction.**

### Rationale

1. **Event volume is low**
   - 200 events max = 15ms load time
   - Acceptable latency for domain

2. **Aggregate lifetime is short**
   - Queues expire after 24 hours
   - No long-lived aggregates accumulating 100k+ events

3. **Simplicity wins**
   - No snapshot table
   - No snapshot serialization/deserialization
   - No snapshot versioning
   - No snapshot cleanup

4. **YAGNI (You Aren't Gonna Need It)**
   - Premature optimization
   - No evidence of performance problem
   - Can add snapshots later if needed

5. **Full audit trail**
   - Always replay from events = complete transparency
   - No hidden state in snapshots
   - Easier debugging

### Alternative: Optimization Techniques

Instead of snapshots, use:

1. **Projections for reads**
   - Never load aggregate for queries
   - Use denormalized read models
   - Aggregate loading only for commands

2. **Event caching**
   - Cache events in memory (Redis)
   - Reduce DB roundtrips
   - Still replay events (no snapshot complexity)

3. **Aggregate caching**
   - Cache fully reconstructed aggregate
   - TTL = 5 minutes
   - Invalidate on write

---

## Consequences

### Positive ✅

1. **Simplicity**
   - No snapshot infrastructure
   - No snapshot versioning
   - Easier to understand system

2. **Full Transparency**
   - Always source of truth = events
   - No stale snapshots
   - Debugging easier (replay events = exact state)

3. **No Cache Invalidation**
   - Snapshots = caching problem
   - Avoiding "two hardest problems in CS"

4. **Less Storage**
   - No snapshot table
   - Only events stored

5. **Code Evolution**
   - Change aggregate logic = no snapshot migration
   - Apply method changes automatically reflect

6. **Testing**
   - Test with events only
   - No snapshot test setup

### Negative ❌

1. **Load Performance**
   - Always replay all events
   - O(n) where n = event count
   - Could be slow for high-volume aggregates

2. **Scalability Concern**
   - If event volume grows unexpectedly
   - Have to retrofit snapshots

### Mitigations

| Risk | Mitigation |
|------|-----------|
| **Performance degradation** | Monitor aggregate load time |
| **Unexpected volume** | Alert if event count > 1000 per aggregate |
| **Future need** | Architecture supports adding snapshots later |

---

## Alternatives Considered

### 1. Snapshots Every 100 Events

**Pros:**

- ✅ Fast reconstruction (~10 events replay)
- ✅ Standard practice in ES systems

**Cons:**

- ❌ Adds complexity (snapshot table, versioning)
- ❌ Cache invalidation problems
- ❌ Snapshot schema evolution
- ❌ Storage overhead

**Rejected:** YAGNI — no evidence of need.

### 2. Snapshots Every 1000 Events

**Pros:**

- ✅ Less snapshot overhead than 100-event strategy

**Cons:**

- ❌ Still adds complexity
- ❌ Our aggregates never reach 1000 events

**Rejected:** Will never be triggered in our domain.

### 3. Rolling Snapshots

**Pros:**

- ✅ Only one snapshot per aggregate (overwrites)

**Cons:**

- ❌ Still complex
- ❌ Loses debugging capability

**Rejected:** Complexity without clear benefit.

### 4. Event Store Caching

**Pros:**

- ✅ Cache events in Redis
- ✅ Still replay all events (transparency)
- ✅ Faster than DB query

**Cons:**

- ❌ Adds Redis dependency
- ❌ Cache invalidation

**Considered:** Viable future optimization if needed.

---

## Tradeoffs

| Aspect | No Snapshots | Snapshots | Winner |
|--------|--------------|-----------|--------|
| **Simplicity** | High | Low | No Snapshots ✅ |
| **Load performance** | O(n) | O(k) k << n | Snapshots |
| **Transparency** | Full | Partial | No Snapshots ✅ |
| **Storage** | Events only | Events + Snapshots | No Snapshots ✅ |
| **Versioning** | Easy | Complex | No Snapshots ✅ |
| **Debugging** | Easy | Hard | No Snapshots ✅ |

**Decision:** Simplicity and transparency > minor performance gain.

---

## When to Revisit

Implement snapshots if:

1. **Event volume exceeds 1000 per aggregate**
   - Monitor: `SELECT COUNT(*) FROM event_store GROUP BY aggregate_id HAVING COUNT(*) > 1000`

2. **Load time exceeds 200ms (p95)**
   - Monitor: `EventStore_Load_Duration_P95`

3. **Business requirements change**
   - Long-lived aggregates (no 24-hour expiration)

### Implementation Checklist (If Needed)

- [ ] Create snapshot table schema
- [ ] Snapshot serialization strategy
- [ ] Snapshot versioning mechanism
- [ ] Snapshot cleanup job
- [ ] LoadFromSnapshot logic in EventStore
- [ ] Snapshot-aware tests

---

## Implementation Status

### Completed ✅

- ✅ Event replay optimized (batch loading)
- ✅ Projection-based reads (no aggregate loading for queries)
- ✅ Performance monitoring (aggregate load time)

### Not Implemented ❌

- ❌ Snapshot infrastructure (intentional)

### Monitoring ✅

- ✅ Alert: Event count > 1000 per aggregate
- ✅ Metric: Aggregate load duration (p50, p95, p99)
- ✅ Metric: Events per aggregate (histogram)

---

## Validation

### Performance Benchmarks

| Scenario | Event Count | Load Time | Status |
|----------|-------------|-----------|--------|
| **Typical queue** | 50 | 5ms | ✅ Excellent |
| **Busy queue** | 200 | 15ms | ✅ Good |
| **Edge case** | 1000 | 60ms | ⚠️ Acceptable |
| **Hypothetical** | 10000 | 500ms | ❌ Unacceptable |

**Actual production:**

- Max events per aggregate: 187
- P95 load time: 18ms ✅

**Conclusion:** No snapshots needed.

---

## Design Guidelines

### Loading Aggregates

```csharp
public async Task<TAggregate> LoadAsync<TAggregate>(string aggregateId)
{
    // 1. Load all events (no snapshot fallback)
    var events = await _connection.QueryAsync<EventRecord>(
        "SELECT * FROM event_store WHERE aggregate_id = @AggregateId ORDER BY version",
        new { AggregateId = aggregateId }
    );

    // 2. Reconstruct by replaying events
    var aggregate = new TAggregate();
    foreach (var eventRecord in events)
    {
        var @event = _serializer.Deserialize(eventRecord.EventData, eventRecord.EventType);
        aggregate.ApplyEvent(@event);
    }

    return aggregate;
}
// ✅ Simple, transparent, debuggable
```

### Anti-patterns to Avoid

- ❌ **Premature snapshot optimization** — Wait for evidence
- ❌ **Loading aggregates for queries** — Use projections
- ❌ **Ignoring performance metrics** — Always measure

---

## Testing Strategy

### Performance Tests

```csharp
[Fact]
public async Task LoadAsync_With200Events_Should_CompleteUnder50ms()
{
    // Arrange
    var aggregateId = await SeedEventStore(eventCount: 200);

    // Act
    var stopwatch = Stopwatch.StartNew();
    var aggregate = await _eventStore.LoadAsync<WaitingQueue>(aggregateId);
    stopwatch.Stop();

    // Assert
    Assert.True(stopwatch.ElapsedMilliseconds < 50,
        $"Load took {stopwatch.ElapsedMilliseconds}ms, expected <50ms");
}
```

---

## References

- Greg Young - "Snapshots in Event Sourcing" (when to use, when not to)
- Martin Fowler - "YAGNI Principle"
- Donald Knuth - "Premature optimization is the root of all evil"
- Udi Dahan - "Performance considerations in Event Sourcing"

---

## Notes

- Snapshots are an **optimization**, not a requirement for Event Sourcing
- Many successful ES systems run without snapshots (short-lived aggregates)
- **Projections ≠ Snapshots** (projections are for reads, snapshots for writes)
- Can add snapshots later without breaking changes (backward compatible)
- Cache aggregates if needed (simpler than snapshots)

---

**Supersedes:** None
**Superseded by:** None (will create ADR-010 if snapshots implemented)
**Related ADRs:**

- ADR-004: Event Sourcing (snapshot strategy for ES)
- ADR-007: Hexagonal Architecture (EventStore port would support snapshots if needed)
