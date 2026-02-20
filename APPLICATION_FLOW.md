# RLAPP — Application Flow

**Descripción paso a paso del flujo de ejecución de casos de uso.**

---

## 🎯 Caso de Uso: Check-In de Paciente

El caso de uso principal es que un paciente se registre en una cola de espera.

### Participantes

- **Actor:** Personal sanitario (nurse) o sistema
- **Agregado:** WaitingQueue
- **Comando:** CheckInPatientCommand
- **Manejador:** CheckInPatientCommandHandler
- **Persistencia:** IEventStore (PostgreSQL)
- **Publicación:** IEventPublisher (Outbox)

---

## 📋 Flujo Detallado

### PASO 1: HTTP Request llega a API

```
POST /api/waiting-room/check-in
Content-Type: application/json

{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "patientName": "Juan Pérez",
  "priority": "High",
  "consultationType": "General",
  "actor": "nurse-001",
  "notes": "Asma aguda"
}
```

### PASO 2: Middleware - CorrelationIdMiddleware

```csharp
// CorrelationIdMiddleware.cs
context.Items["CorrelationId"] =
    context.Request.Headers["X-Correlation-Id"].FirstOrDefault()
    ?? Guid.NewGuid().ToString();

// corr-id = "f47ac10b-58cc-4372-a567-0e02b2c3d479"
// Available for all downstream handlers
```

**Propósito:** Inyectar ID de rastreo para logs distribuidos.

### PASO 3: Endpoint Handler

```csharp
// Program.cs
app.MapPost("/api/waiting-room/check-in", async (
    CheckInPatientDto dto,                      // ← Binding automático del JSON
    HttpContext httpContext,                    // ← Inyectado por ASP.NET
    CheckInPatientCommandHandler handler,       // ← Inyectado del DI
    ILogger<Program> logger,                    // ← Inyectado del DI
    CancellationToken cancellationToken) =>
{
    var correlationId = httpContext.Items["CorrelationId"]?.ToString()
        ?? Guid.NewGuid().ToString();

    logger.LogInformation(
        "CheckIn request received. CorrelationId: {CorrelationId}, " +
        "QueueId: {QueueId}, PatientId: {PatientId}",
        correlationId, dto.QueueId, dto.PatientId);

    // Mapear DTO → Command
    var command = new CheckInPatientCommand
    {
        QueueId = dto.QueueId,
        PatientId = dto.PatientId,
        PatientName = dto.PatientName,
        Priority = dto.Priority,
        ConsultationType = dto.ConsultationType,
        Notes = dto.Notes,
        Actor = dto.Actor,
        CorrelationId = correlationId  // ← Propagar para tracing
    };

    // Delegar al handler
    var eventCount = await handler.HandleAsync(command, cancellationToken);

    logger.LogInformation(
        "CheckIn completed. CorrelationId: {CorrelationId}, " +
        "EventCount: {EventCount}",
        correlationId, eventCount);

    return Results.Ok(new
    {
        Success = true,
        Message = "Patient checked in successfully",
        CorrelationId = correlationId,
        EventCount = eventCount
    });
})
.WithName("CheckInPatient")
.Produces(200)
.Produces(400)
.Produces(404)
.Produces(409)
.Produces(500);
```

**Lo que sucede:**

1. ASP.NET bindea JSON a DTO automáticamente
2. Inyecta dependencias del Container
3. Mapea DTO a Command
4. Llama al handler
5. Retorna respuesta HTTP

### PASO 4: CheckInPatientCommandHandler

```csharp
public sealed class CheckInPatientCommandHandler
{
    private readonly IEventStore _eventStore;
    private readonly IEventPublisher _eventPublisher;
    private readonly IClock _clock;

    public async Task<int> HandleAsync(
        CheckInPatientCommand command,
        CancellationToken cancellationToken = default)
    {
        // ═══════════════════════════════════════════════════════════════
        // PASO 4A: CARGA EL AGREGADO (Reconstrye desde eventos)
        // ═══════════════════════════════════════════════════════════════

        var queue = await _eventStore.LoadAsync(command.QueueId, cancellationToken)
            ?? throw new AggregateNotFoundException(command.QueueId);

        // En EventStore.LoadAsync():
        //   1. SELECT * FROM waiting_room_events WHERE aggregate_id = 'QUEUE-01'
        //   2. Foreach evento: call queue.ApplyEvent(evento)
        //   3. Retorna queue con estado reconstruido
        //
        // Resultado:
        //   queue.Id = "QUEUE-01"
        //   queue.Version = 2 (si hay 2 eventos)
        //   queue.Patients = [PAT-001]


        // ═══════════════════════════════════════════════════════════════
        // PASO 4B: CREA METADATOS PARA AUDITORIA
        // ═══════════════════════════════════════════════════════════════

        var metadata = EventMetadata.CreateNew(
            aggregateId: command.QueueId,
            actor: command.Actor,                          // "nurse-001"
            correlationId: command.CorrelationId           // Propagar para tracing
                ?? Guid.NewGuid().ToString());

        // Resultado:
        // {
        //   EventId: "e47ac10b-58cc-4372-a567-0e02b2c3d479",
        //   AggregateId: "QUEUE-01",
        //   Version: (será set por EventStore),
        //   CorrelationId: "f47ac10b-58cc-4372-a567-0e02b2c3d479",
        //   CausationId: (mismo que EventId),
        //   Actor: "nurse-001",
        //   OccurredAt: DateTime.UtcNow,
        //   IdempotencyKey: Guid.NewGuid()
        // }


        // ═══════════════════════════════════════════════════════════════
        // PASO 4C: CREA VALUE OBJECTS (Validación)
        // ═══════════════════════════════════════════════════════════════

        var patientId = PatientId.Create(command.PatientId);
        // Si command.PatientId es vacío → throws DomainException

        var priority = Priority.Create(command.Priority);
        // Si "High" → normaliza a "High" (canonical)
        // Si "URGENTE" → throws DomainException

        var consultationType = ConsultationType.Create(command.ConsultationType);
        // Si length < 2 o > 100 → throws DomainException


        // ═══════════════════════════════════════════════════════════════
        // PASO 4D: EJECUTA LOGICA DE DOMINIO (Agregado)
        // ═══════════════════════════════════════════════════════════════

        queue.CheckInPatient(
            patientId: patientId,
            patientName: command.PatientName,
            priority: priority,
            consultationType: consultationType,
            checkInTime: _clock.UtcNow,
            metadata: metadata,
            notes: command.Notes);

        // En WaitingQueue.CheckInPatient():
        //   1. Validate invariants:
        //      - Capacity check: currentCount < maxCapacity
        //      - Duplicate check: patientId not in queue
        //      - Priority validation
        //   2. If any fail → throw DomainException
        //   3. If all pass:
        //      - Create PatientCheckedIn event
        //      - Call RaiseEvent(event)
        //        → ApplyEvent(event) [updates state]
        //        → _uncommittedEvents.Add(event)
        //      - queue.Patients now includes PAT-001
        //      - queue.Version = 3

        // Si violación de regla → exception propaga al endpoint
        // Endpoint → ExceptionHandlerMiddleware:
        //   - DomainException → 400 Bad Request
        //   - Mensaje: "Queue is at maximum capacity (20). Cannot add more patients."


        // ═══════════════════════════════════════════════════════════════
        // PASO 4E: PERSISTENCIA ATOMICA (EventStore + Outbox)
        // ═══════════════════════════════════════════════════════════════

        var eventsToPublish = queue.UncommittedEvents.ToList();

        await _eventStore.SaveAsync(queue, cancellationToken);

        // En PostgresEventStore.SaveAsync(queue):
        //   1. BEGIN TRANSACTION
        //   2. Get current version from DB:
        //      SELECT COALESCE(MAX(version), 0) FROM waiting_room_events
        //      WHERE aggregate_id = 'QUEUE-01'
        //      → currentVersion = 2
        //   3. Check version conflict:
        //      expectedVersion = queue.Version - uncommitted.Count
        //                      = 3 - 1 = 2
        //      If currentVersion != expectedVersion → throw EventConflictException
        //      (Concurrent modification detected)
        //   4. Insert events:
        //      INSERT INTO waiting_room_events (
        //        event_id, aggregate_id, version, event_name,
        //        occurred_at, correlation_id, causation_id, actor,
        //        idempotency_key, schema_version, payload
        //      ) VALUES (
        //        'e47ac10b-...', 'QUEUE-01', 3, 'PatientCheckedIn',
        //        '2026-02-19T10:05:00Z', 'f47ac10b-...', 'e47ac10b-...',
        //        'nurse-001', 'idempotency-...', 1,
        //        '{"queueId":"QUEUE-01","patientId":"PAT-001",...}'
        //      )
        //      ON CONFLICT (idempotency_key) DO NOTHING
        //      (Idempotencia: si se reintenta, no duplica)
        //   5. Insert outbox messages (SAME TX):
        //      INSERT INTO waiting_room_outbox (
        //        outbox_id, event_id, event_name, occurred_at,
        //        correlation_id, causation_id, payload,
        //        status, attempts, next_attempt_at, last_error
        //      ) VALUES (
        //        ..., 'e47ac10b-...', 'PatientCheckedIn', ...,
        //        'Pending', 0, NULL, NULL
        //      )
        //      ON CONFLICT (event_id) DO NOTHING
        //   6. COMMIT TRANSACTION (all or nothing)
        //   7. queue.ClearUncommittedEvents()


        // ═══════════════════════════════════════════════════════════════
        // PASO 4F: PUBLICACION (Outbox - No-op en API)
        // ═══════════════════════════════════════════════════════════════

        if (eventsToPublish.Count > 0)
        {
            await _eventPublisher.PublishAsync(eventsToPublish, cancellationToken);
        }

        // En API: OutboxEventPublisher.PublishAsync() → no-op (returns immediately)
        // En Worker: RabbitMqEventPublisher.PublishAsync() → actual publish
        //
        // Razón: Separación clara de responsabilidades
        // - API se enfoca en escribir rápido
        // - Worker se enfoca en distribución confiable


        return eventsToPublish.Count;
    }
}
```

### PASO 5: Return HTTP Response

```json
200 OK
{
  "success": true,
  "message": "Patient checked in successfully",
  "correlationId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "eventCount": 1
}
```

**Tiempo de respuesta:** ~50-100 ms (más rápido porque Outbox dispatch es async)

---

## 🔄 Flujo Asincrónico (OutboxWorker)

**Mientras el cliente está fuera (segundos después del check-in):**

### PASO 6: OutboxWorker.ExecuteAsync() (BackgroundService)

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            // Cada 5 segundos (~configurable)
            var dispatchedCount = await _dispatcher.DispatchBatchAsync(stoppingToken);

            if (dispatchedCount == 0)
            {
                _logger.LogDebug("No messages dispatched");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in dispatcher loop. Continuing...");
        }

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}
```

### PASO 7: OutboxDispatcher.DispatchBatchAsync()

```csharp
public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken = default)
{
    // STEP A: Fetch pending messages
    var messages = await _outboxStore.GetPendingAsync(
        batchSize: 100,
        cancellationToken);

    // En PostgresOutboxStore.GetPendingAsync():
    //   SELECT * FROM waiting_room_outbox
    //   WHERE status = 'Pending'
    //     AND (next_attempt_at IS NULL OR next_attempt_at <= NOW())
    //   ORDER BY occurred_at
    //   LIMIT 100;
    //
    // Resultado: [OutboxMessage { eventId: ..., payload: ... }]

    var successCount = 0;

    foreach (var message in messages)
    {
        try
        {
            // STEP B: Dispatch single message
            await DispatchSingleMessageAsync(message, cancellationToken);
            successCount++;
        }
        catch (Exception ex)
        {
            // STEP C: Handle failure with retry
            await HandleFailureAsync(message, ex, cancellationToken);
        }
    }

    _logger.LogInformation(
        "Dispatched {SuccessCount}/{TotalCount} messages",
        successCount, messages.Count);

    return successCount;
}
```

### PASO 8: DispatchSingleMessageAsync()

```csharp
private async Task DispatchSingleMessageAsync(
    OutboxMessage message,
    CancellationToken cancellationToken)
{
    var dispatchStart = DateTime.UtcNow;

    // STEP A: Deserialize event
    var domainEvent = _serializer.Deserialize(
        message.EventName,           // "PatientCheckedIn"
        message.Payload);            // JSON

    // En EventSerializer.Deserialize():
    //   1. Get type from registry: "PatientCheckedIn" → typeof(PatientCheckedIn)
    //   2. JsonConvert.DeserializeObject(json, typeof(PatientCheckedIn))
    //   3. Retorna DomainEvent (strongly typed)

    // STEP B: Publish to RabbitMQ
    await _publisher.PublishAsync(domainEvent, cancellationToken);

    // En RabbitMqEventPublisher.PublishAsync():
    //   1. Create connection to RabbitMQ (localhost:5672)
    //   2. Create channel
    //   3. Declare exchange: "waiting_room_events" (topic)
    //   4. Publish message:
    //      - RoutingKey: "PatientCheckedIn"
    //      - Body: JSON serialized
    //      - Properties:
    //          CorrelationId: metadata.CorrelationId (for tracing)
    //          MessageId: metadata.IdempotencyKey (for deduplication)
    //   5. Close connection

    // STEP C: Mark as dispatched
    await _outboxStore.MarkDispatchedAsync(
        new[] { message.EventId },
        cancellationToken);

    // En PostgresOutboxStore.MarkDispatchedAsync():
    //   UPDATE waiting_room_outbox
    //   SET status = 'Dispatched',
    //       attempts = attempts + 1,
    //       next_attempt_at = NULL,
    //       last_error = NULL
    //   WHERE event_id = 'e47ac10b-...';

    _logger.LogInformation(
        "Successfully dispatched event {EventId} - {EventName}",
        message.EventId, message.EventName);
}
```

---

## 🎯 Flujo de Proyecciones

**Cuando RabbitMQ distribuye el evento:**

### PASO 9: ProjectionEventProcessor

```csharp
public async Task ProcessEventAsync(
    DomainEvent @event,
    CancellationToken cancellation = default)
{
    var startTime = DateTime.UtcNow;

    // STEP A: Log event reception
    _logger.LogDebug(
        "Processing event {EventType} (aggregate: {AggregateId})",
        @event.GetType().Name,
        @event.Metadata.AggregateId);

    try
    {
        // STEP B: Delegate to projection engine
        await _projection.ProcessEventAsync(@event, cancellation);

        // En WaitingRoomProjectionEngine.ProcessEventAsync():
        //   1. Find handler for PatientCheckedIn
        //   2. Call handler.HandleAsync(event)
        //   3. Update checkpoint

        // STEP C: Record success metrics
        var processingDurationMs = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

        await _lagTracker.RecordEventProcessedAsync(
            eventId: @event.Metadata.EventId,
            processedAt: DateTime.UtcNow,
            processingDurationMs: processingDurationMs,
            cancellation: cancellation);

        _logger.LogInformation(
            "Successfully processed event {EventType} (duration: {Duration}ms)",
            @event.GetType().Name,
            processingDurationMs);
    }
    catch (Exception ex)
    {
        // STEP D: Handle failure
        await _lagTracker.RecordEventFailedAsync(...);
        throw;
    }
}
```

### PASO 10: WaitingRoomProjectionEngine.ProcessEventAsync()

```csharp
public async Task ProcessEventAsync(
    DomainEvent @event,
    CancellationToken cancellationToken = default)
{
    await ProcessEventInternalAsync(@event, cancellationToken);

    // Update checkpoint for progress tracking
    var checkpoint = new ProjectionCheckpoint
    {
        ProjectionId = ProjectionId,
        LastEventVersion = @event.Metadata.Version,
        CheckpointedAt = DateTimeOffset.UtcNow,
        IdempotencyKey = Guid.NewGuid().ToString(),
        Status = "processing"
    };

    await _context.SaveCheckpointAsync(checkpoint, cancellationToken);
}

private async Task ProcessEventInternalAsync(
    DomainEvent @event,
    CancellationToken cancellationToken)
{
    var handlerName = @event.EventName;  // "PatientCheckedIn"

    if (!_handlers.TryGetValue(handlerName, out var handler))
    {
        _logger.LogWarning(
            "No handler found for event {EventName}",
            handlerName);
        return;
    }

    // Elegi un handler
    await handler.HandleAsync(@event, _context, cancellationToken);
}
```

### PASO 11: PatientCheckedInProjectionHandler.HandleAsync()

```csharp
public async Task HandleAsync(
    DomainEvent @event,
    IProjectionContext context,
    CancellationToken cancellationToken = default)
{
    if (@event is not PatientCheckedIn evt)
        throw new ArgumentException("Expected PatientCheckedIn");

    var waitingContext = (IWaitingRoomProjectionContext)context;

    // STEP A: Generate idempotency key
    var idempotencyKey = GenerateIdempotencyKey(evt);
    // "patient-checked-in:QUEUE-01:<aggregateId>:<eventId>"

    // STEP B: Check idempotency
    if (await context.AlreadyProcessedAsync(idempotencyKey, cancellationToken))
        return;  // Skip if already handled

    // STEP C: Update MonitorView
    await waitingContext.UpdateMonitorViewAsync(
        queueId: evt.QueueId,
        priority: NormalizePriority(evt.Priority),  // "high" → "High"
        operation: "increment",                      // Count++
        cancellationToken);

    // En InMemoryWaitingRoomProjectionContext.UpdateMonitorViewAsync():
    //   Get or create view
    //   _views[queueId + ":monitor"]
    //   Increment counter for "High" priority
    //   _monitorViews[evt.QueueId].HighPriorityCount++

    // STEP D: Update QueueStateView
    await waitingContext.AddPatientToQueueAsync(
        queueId: evt.QueueId,
        patient: new PatientInQueueDto
        {
            PatientId = evt.PatientId,
            PatientName = evt.PatientName,
            Priority = NormalizePriority(evt.Priority),
            CheckInTime = evt.Metadata.OccurredAt,
            WaitTimeMinutes = 0
        },
        cancellationToken);

    // En InMemoryWaitingRoomProjectionContext.AddPatientToQueueAsync():
    //   Get or create QueueStateView
    //   Add PatientInQueueDto to Patients[]
    //   _queueStates[evt.QueueId].Patients.Add(...)

    // STEP E: Mark as processed
    await context.MarkProcessedAsync(idempotencyKey, cancellationToken);

    // En context.MarkProcessedAsync():
    //   Add idempotencyKey to _processedKeys set
    //   (prevents reprocessing if event is retried)
}
```

---

## 📊 Modelo de Excepción

### Mapa de Excepciones → HTTP Status

```
Domain Layer (business rule violation)
  ↓
└─ DomainException
   └─ Propagate → Application
      └─ CheckInPatientCommandHandler catches implicitly
         └─ Bubbles to Presentation

Presentation Layer (ExceptionHandlerMiddleware)
  ↓
  If DomainException
    └─ HTTP 400 Bad Request
       {
         "error": "DomainViolation",
         "message": "Queue is at maximum capacity..."
       }

Application Layer (custom)
  ↓
  If AggregateNotFoundException
    └─ HTTP 404 Not Found
       {
         "error": "AggregateNotFound",
         "message": "Aggregate with ID 'QUEUE-01' not found..."
       }

  If EventConflictException
    └─ HTTP 409 Conflict
       {
         "error": "ConcurrencyConflict",
         "message": "The resource was modified by another request..."
       }

Infrastructure (unexpected)
  ↓
  If any other exception
    └─ HTTP 500 Internal Server Error
       {
         "error": "InternalServerError",
         "message": "An unexpected error occurred..."
       }
```

### Flujo Completo de Error

**Caso: Cola llena**

```
1. API receives request
2. Handler loads queue (2 events replayed, 20 patients)
3. queue.CheckInPatient() called
4. WaitingQueueInvariants.ValidateCapacity()
   └─ throw new DomainException("Queue is at maximum...")
5. Exception bubbles up (not caught)
6. ExceptionHandlerMiddleware catches
7. Maps to HTTP 400
8. Returns error response
9. Client receives 400 with message
```

---

## 🔐 Idempotencia

### Garantía de Idempotencia

**Nivel 1: EventStore**

```csharp
INSERT INTO waiting_room_events (...)
ON CONFLICT (idempotency_key) DO NOTHING;
```

Si mismo comando se ejecuta 2x con mismo idempotency_key → no duplica evento.

**Nivel 2: Outbox**

```csharp
INSERT INTO waiting_room_outbox (...)
ON CONFLICT (event_id) DO NOTHING;
```

Si mensaje se procesa 2x → no duplica en outbox.

**Nivel 3: Projections**

```csharp
if (await context.AlreadyProcessedAsync(idempotencyKey, cancellation))
    return;  // Skip
```

Si evento llega 2x a proyección → handler es idempotente (memoization).

---

## 📈 Performance Characteristics

| Operación | Tiempo Típico | Bottleneck |
|-----------|---------------|-----------|
| EndPoint (sync) | 50-100 ms | EventStore load/save |
| Outbox dispatch | 100-500 ms | RabbitMQ network |
| Projection update | 10-50 ms | In-memory operation |
| Total (latency) | 50-100 ms (API response only) | - |
| Total (end-to-end) | 100-200 ms (projection updated) | Async processing |

**Optimización:** API responde rápido porque projection update es asincrónico.

---

**Última actualización:** Febrero 2026
