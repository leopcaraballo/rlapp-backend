# Phase 7: Event Lag Metrics Bug Fix

**Status:** ✅ COMPLETE - All 65 tests passing

## Issues Fixed

### 1. **Dapper Dynamic Property Mapping (Case Sensitivity)**

**Problem:**

- PostgreSQL returns column names in lowercase (e.g., `totaleventsprocessed`)
- C# code was accessing properties with PascalCase (e.g., `result.TotalEventsProcessed`)
- Dapper uses lowercase names for dynamic object properties
- This caused all lag metrics to be `null`, resulting in `0` values being returned

**Root Cause:**

```sql
SELECT COUNT(*) as TotalEventsProcessed, ...
```

PostgreSQL downcases the alias to `totaleventsprocessed`, but C# tried to access `result.TotalEventsProcessed` (undefined property).

**Fix:**
Changed all property accesses in `GetStatisticsAsync` to use lowercase:

- `result.TotalEventsProcessed` → `result.totaleventsprocessed`
- `result.AverageLagMs` → `result.averagelagms`
- `result.P95LagMs` → `result.p95lagms`
- etc.

**File:** [PostgresEventLagTracker.cs](src/Services/WaitingRoom/WaitingRoom.Infrastructure/Observability/PostgresEventLagTracker.cs) Line 224-261

---

### 2. **Milliseconds Calculation Order (Operator Precedence)**

**Problem:**

- Lag calculation was: `EXTRACT(EPOCH FROM (...))::INT * 1000`
- Since `EXTRACT` returns seconds as decimal (e.g., 0.068), casting to INT first gives 0
- Then `0 * 1000 = 0`
- Result: All lag metrics were `0` instead of correct milliseconds

**Incorrect:**

```sql
total_lag_ms = EXTRACT(EPOCH FROM (@ProcessedAt - event_created_at))::INT * 1000
-- 0.068 seconds → cast to INT = 0 → 0 * 1000 = 0
```

**Correct:**

```sql
total_lag_ms = (EXTRACT(EPOCH FROM (@ProcessedAt - event_created_at)) * 1000)::INT
-- 0.068 seconds → 0.068 * 1000 = 68 → cast to INT = 68
```

**File:** [PostgresEventLagTracker.cs](src/Services/WaitingRoom/WaitingRoom.Infrastructure/Observability/PostgresEventLagTracker.cs) Line 110

---

### 3. **Idempotent Event Creation Resets Processing State**

**Problem:**

- When processing the same event twice (for idempotency):
  1. First `ProcessEventAsync`: Creates lag record with status='CREATED', then updates to 'PROCESSED'
  2. Second `ProcessEventAsync`: Calls `RecordEventCreatedAsync` which has `ON CONFLICT (event_id) DO UPDATE SET status = 'CREATED'`
     - This RESETS status from 'PROCESSED' back to 'CREATED'
     - Then the subsequent `RecordEventProcessedAsync` UPDATE executes (because status != 'PROCESSED' is now true)
     - This recalculates `total_lag_ms` with a NEW timestamp
- Result: Idempotency test fails because metrics change on second processing

**Issue Chain:**

```
ProcessEventAsync call 1:
├─ RecordEventCreatedAsync: INSERT with status='CREATED'
└─ RecordEventProcessedAsync: UPDATE status = 'PROCESSED', total_lag_ms = 30ms

ProcessEventAsync call 2:
├─ RecordEventCreatedAsync: ON CONFLICT DO UPDATE status = 'CREATED' ← RESETS!
├─ RecordEventProcessedAsync: UPDATE (now possible since status='CREATED')
│  └─ total_lag_ms = 50ms ← CHANGED!
└─ GetLagMetrics returns new value: NOT idempotent!
```

**Fix:**
Changed `ON CONFLICT` to `DO NOTHING` instead of updating:

```sql
ON CONFLICT (event_id) DO NOTHING
```

This makes `RecordEventCreatedAsync` completely idempotent - if the event already exists, nothing changes.

**File:** [PostgresEventLagTracker.cs](src/Services/WaitingRoom/WaitingRoom.Infrastructure/Observability/PostgresEventLagTracker.cs) Line 43

---

### 4. **Lag Metrics Update on Reprocessing**

**Problem:**

- Secondary processing of an event would recalculate `total_lag_ms` with the new timestamp
- This violates idempotency: the same event processing should never change recorded metrics

**Fix:**
Added condition to UPDATE only if status != 'PROCESSED':

```sql
UPDATE event_processing_lag
SET total_lag_ms = ...
WHERE event_id = @EventId
  AND status != 'PROCESSED'  -- Only update first time
```

**File:** [PostgresEventLagTracker.cs](src/Services/WaitingRoom/WaitingRoom.Infrastructure/Observability/PostgresEventLagTracker.cs) Line 117

---

## Test Results

### Before Fixes

- **64 passing, 1 failing** (LagStatistics_MultipleEvents_ComputedCorrectly)
- Lag metrics returned 0 instead of actual values
- Idempotency test failed due to metric changes

### After Fixes

- **65/65 passing** ✅
  - WaitingRoom.Tests.Application: 7/7
  - WaitingRoom.Tests.Domain: 39/39
  - WaitingRoom.Tests.Projections: 9/9
  - WaitingRoom.Tests.Integration: 10/10

## Impact Assessment

| Area | Impact | Severity |
|------|--------|----------|
| Lag Metrics Accuracy | CRITICAL | High - Observability completely broken |
| Event Processing | NONE | - Core pipeline unaffected |
| Idempotency | IMPORTANT | Medium - Edge case but important for reliability |
| Schema | NONE | - No schema changes needed |
| Performance | NONE | - Minimal query changes |

## Files Modified

1. [PostgresEventLagTracker.cs](src/Services/WaitingRoom/WaitingRoom.Infrastructure/Observability/PostgresEventLagTracker.cs)
   - Line 43: Changed `ON CONFLICT ... DO UPDATE` to `DO NOTHING`
   - Line 110: Fixed milliseconds calculation order
   - Line 117: Added idempotency condition to UPDATE
   - Lines 224-261: Changed result property access to lowercase

## Verification

All tests pass with clean database state:

```bash
TRUNCATE TABLE event_processing_lag CASCADE;
dotnet test -c Release

Result: 65 passed, 0 failed
```

## Lessons Learned

1. **Dapper Dynamic Typing**: When using `QueryFirstOrDefaultAsync` without explicit type mapping, PostgreSQL column names are lowercased
2. **SQL Operator Precedence**: Always be explicit with parentheses in compound expressions
3. **Idempotency @ DB Level**: Use `DO NOTHING` or conditional logic for true idempotency
4. **Test Data Isolation**: Ensure tests truncate tables in `IAsyncLifetime.InitializeAsync`

## Next Phase

All core functionality is now working correctly. Recommend:

- Monitoring event lag metrics in production
- Adding alerting for high lag percentiles
- Performance testing with realistic event volumes
