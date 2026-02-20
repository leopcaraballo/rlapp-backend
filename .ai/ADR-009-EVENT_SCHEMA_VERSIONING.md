# ADR-009: Event Schema Versioning Strategy

**Date:** 2026-02-19
**Status:** ACCEPTED
**Context:** Managing event schema evolution in Event Sourcing
**Decision Makers:** Enterprise Architect Team

---

## Context

### Problem

Event Sourcing systems store events as immutable facts:

```
┌────────────────────────────────────────┐
│  Event Store (append-only)             │
│                                        │
│  PatientCheckedIn v1 (2024-01-01)     │
│  PatientCheckedIn v1 (2024-01-15)     │
│  PatientCheckedIn v1 (2024-02-03)     │
│  ...1 million events                  │
└────────────────────────────────────────┘
```

**What happens when:**

- ✅ Add new field to event?
- ✅ Remove field from event?
- ✅ Rename field?
- ✅ Change field type?
- ✅ Split event into two events?

**Real scenario:**

```csharp
// v1 — Initial implementation
public sealed record PatientCheckedIn
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string PatientName { get; init; }
}

// v2 — Business requests: Need patient phone number
public sealed record PatientCheckedIn
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string PatientName { get; init; }
    public string PhoneNumber { get; init; }  // ← NEW FIELD
}

// ❓ What about 1 million events stored with v1 schema?
```

**Two strategies:**

1. **Upcasting** — Convert old events to new schema on read
2. **Versioned Events** — Store version, handle multiple schemas

---

## Decision

**Use weak schema versioning with upcasting for additive changes.**

### Strategy

#### 1. Event Versioning Convention

```csharp
// Always include Version in metadata (not in event itself)
public sealed record EventMetadata
{
    public int Version { get; init; } = 1;  // Default v1
    public string EventType { get; init; }
    public DateTime Timestamp { get; init; }
    public string CorrelationId { get; init; }
}
```

#### 2. Additive Changes (No Migration)

**Allowed:**

- ✅ Add new optional field
- ✅ Add new event type

```csharp
// v1 → v2: Add new field with default value
public sealed record PatientCheckedInV1
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string PatientName { get; init; }
}

// v2: Add PhoneNumber (optional)
public sealed record PatientCheckedIn
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string PatientName { get; init; }
    public string? PhoneNumber { get; init; }  // ← Nullable = backward compatible
}

// Upcasting
public PatientCheckedIn Upcast(PatientCheckedInV1 old)
{
    return new PatientCheckedIn
    {
        QueueId = old.QueueId,
        PatientId = old.PatientId,
        PatientName = old.PatientName,
        PhoneNumber = null  // ← Default for old events
    };
}
```

#### 3. Breaking Changes (Explicit Versioning)

**When needed:**

- ❌ Remove field (breaking)
- ❌ Rename field (breaking)
- ❌ Change type (breaking)

**Solution:**

```csharp
// Keep v1 for deserialization
public sealed record PatientCheckedInV1
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string PatientName { get; init; }  // ← Will be renamed
}

// v2: New version with renamed field
public sealed record PatientCheckedInV2
{
    public string QueueId { get; init; }
    public string PatientId { get; init; }
    public string FullName { get; init; }  // ← Renamed from PatientName
}

// Upcaster
public class PatientCheckedInUpcaster : IEventUpcaster
{
    public DomainEvent Upcast(DomainEvent @event, int version)
    {
        return version switch
        {
            1 => UpcastV1ToV2((PatientCheckedInV1)@event),
            2 => @event,  // Already current version
            _ => throw new UnsupportedEventVersionException(version)
        };
    }

    private PatientCheckedInV2 UpcastV1ToV2(PatientCheckedInV1 v1)
    {
        return new PatientCheckedInV2
        {
            QueueId = v1.QueueId,
            PatientId = v1.PatientId,
            FullName = v1.PatientName  // ← Map old field to new
        };
    }
}
```

#### 4. Event Serialization with Version

```json
{
  "eventId": "evt_12345",
  "aggregateId": "queue-001",
  "eventType": "PatientCheckedIn",
  "version": 1,
  "timestamp": "2024-01-15T10:30:00Z",
  "data": {
    "queueId": "queue-001",
    "patientId": "pt-123",
    "patientName": "John Doe"
  }
}
```

---

## Consequences

### Positive ✅

1. **Backward Compatibility**
   - Old events readable forever
   - No data migration required
   - Append-only integrity preserved

2. **Forward Evolution**
   - Add fields without breaking old events
   - Gradual schema evolution
   - No downtime

3. **Replay Safety**
   - Rebuild projections from all historical events
   - Upcasting happens transparently
   - Consistent behavior

4. **Debugging**
   - Original event preserved
   - Can debug v1 events even after v2 deployed
   - Full audit trail

5. **Multiple Versions Coexist**
   - v1 and v2 events in same store
   - No forced migration
   - Organic transition

### Negative ❌

1. **Upcasting Complexity**
   - Need upcaster for each breaking change
   - Logic to maintain
   - Potential bugs in transformation

2. **Performance**
   - Upcasting on every load (O(n) events)
   - Could cache upcasted events
   - Minor overhead

3. **Multiple Event Classes**
   - PatientCheckedInV1, PatientCheckedInV2, ...
   - Code clutter
   - Namespace pollution

4. **Testing**
   - Must test all versions
   - Upcaster tests required
   - Regression risk

### Mitigations

| Risk | Mitigation |
|------|-----------|
| **Upcasting bugs** | Comprehensive unit tests for each upcaster |
| **Performance** | Cache upcasted aggregates (not events) |
| **Code clutter** | Archive old versions in `/Legacy` namespace |
| **Testing overhead** | Automated test generation for all versions |

---

## Alternatives Considered

### 1. No Versioning (Tightly Coupled Schema)

**Pros:**

- ✅ Simple

**Cons:**

- ❌ Breaking changes require data migration
- ❌ Cannot replay old events
- ❌ Violates immutability

**Rejected:** Defeats purpose of Event Sourcing.

### 2. Copy-and-Transform Migration

**Pros:**

- ✅ All events in current schema

**Cons:**

- ❌ Mutates event store (violates immutability)
- ❌ Loses historical accuracy
- ❌ Risky (data loss potential)
- ❌ Downtime required

**Rejected:** Violates core ES principle.

### 3. Dual Storage (Old + New)

**Pros:**

- ✅ Both versions available

**Cons:**

- ❌ Doubles storage
- ❌ Synchronization complexity
- ❌ Two sources of truth

**Rejected:** Over-engineered.

### 4. Event Store Built-in Versioning

**Pros:**

- ✅ Framework handles it (EventStore DB)

**Cons:**

- ❌ Couples to specific DB
- ❌ We use PostgreSQL (no built-in versioning)

**Rejected:** Vendor lock-in.

---

## Tradeoffs

| Aspect | No Versioning | Upcasting | Winner |
|--------|---------------|-----------|--------|
| **Simplicity** | High | Medium | No Versioning |
| **Backward compat** | None | Full | Upcasting ✅ |
| **Immutability** | Violated | Preserved | Upcasting ✅ |
| **Replay safety** | Broken | Safe | Upcasting ✅ |
| **Migration effort** | High | Low | Upcasting ✅ |

**Decision:** Upcasting essential for Event Sourcing integrity.

---

## Implementation Status

### Completed ✅

- ✅ `EventMetadata` with version field
- ✅ JSON serialization includes version
- ✅ Additive change example (Priority field added as nullable)

### In Progress 🚧

- 🚧 Formal upcaster interface

### Future 📅

- 📅 Event versioning CI/CD check (detect breaking changes)
- 📅 Automated upcaster test generation
- 📅 Event schema registry

---

## Design Guidelines

### Adding a Field (Non-Breaking)

```csharp
// ✅ GOOD: Nullable/Optional field
public sealed record PatientCheckedIn
{
    public string QueueId { get; init; }
    public string? PhoneNumber { get; init; }  // ← Nullable = backward compatible
}
```

### Renaming a Field (Breaking)

```csharp
// 1. Keep v1 for deserialization
public sealed record PatientCheckedInV1
{
    public string PatientName { get; init; }
}

// 2. Create v2 with new field name
public sealed record PatientCheckedInV2
{
    public string FullName { get; init; }  // ← Renamed
}

// 3. Create upcaster
public PatientCheckedInV2 Upcast(PatientCheckedInV1 v1)
{
    return new PatientCheckedInV2 { FullName = v1.PatientName };
}
```

### Removing a Field (Breaking)

```csharp
// 1. Keep v1 for deserialization
public sealed record PatientCheckedInV1
{
    public string ObsoleteField { get; init; }  // ← Will be removed
}

// 2. Create v2 without field
public sealed record PatientCheckedInV2
{
    // ObsoleteField removed
}

// 3. Upcaster ignores field
public PatientCheckedInV2 Upcast(PatientCheckedInV1 v1)
{
    return new PatientCheckedInV2 { /* ObsoleteField dropped */ };
}
```

### Anti-patterns to Avoid

- ❌ **Mutating stored events** — Never modify event store
- ❌ **Force migration** — Upcasting is transparent, not forced
- ❌ **Version in event data** — Version in metadata only
- ❌ **Leaking version to domain** — Aggregate sees only current version

---

## Testing Strategy

### Upcaster Tests

```csharp
[Fact]
public void Upcaster_Should_ConvertV1ToV2()
{
    // Arrange
    var v1 = new PatientCheckedInV1
    {
        QueueId = "queue-001",
        PatientId = "pt-123",
        PatientName = "John Doe"
    };

    var upcaster = new PatientCheckedInUpcaster();

    // Act
    var v2 = upcaster.UpcastV1ToV2(v1);

    // Assert
    Assert.Equal(v1.QueueId, v2.QueueId);
    Assert.Equal(v1.PatientId, v2.PatientId);
    Assert.Equal(v1.PatientName, v2.FullName);  // ← Field renamed
}
```

### Replay Tests

```csharp
[Fact]
public async Task Replay_Should_HandleMixedVersions()
{
    // Arrange
    await SeedEventStore(new[]
    {
        new PatientCheckedInV1 { /* ... */ },  // Old event
        new PatientCheckedInV2 { /* ... */ }   // New event
    });

    // Act
    var aggregate = await _eventStore.LoadAsync<WaitingQueue>(queueId);

    // Assert
    Assert.Equal(2, aggregate.PatientCount);  // Both versions applied
}
```

---

## Monitoring

### Metrics

| Metric | Purpose |
|--------|---------|
| **Event version distribution** | How many v1 vs v2 events exist |
| **Upcasting errors** | Detect transformation bugs |
| **Replay duration** | Detect performance degradation |

---

## References

- Greg Young - "Versioning in an Event Sourced System"
- Vaughn Vernon - "Implementing Domain-Driven Design" (Ch. 8)
- Martin Fowler - "SchemaVersioningPatterns"
- EventStore documentation - "Event Versioning"

---

## Notes

- **Weak schema** = JSON with flexible deserialization (tolerant reader)
- **Strong schema** = Protobuf/Avro with strict contracts (less flexible)
- We chose weak schema for flexibility
- Upcasting happens at **deserialization time** (not storage time)
- Events are **versioned**, not aggregates
- Version number is **metadata**, not part of event data

---

**Supersedes:** None
**Superseded by:** None
**Related ADRs:**

- ADR-004: Event Sourcing (events are immutable facts)
- ADR-008: No Snapshot Strategy (upcasting applies to all events on replay)
