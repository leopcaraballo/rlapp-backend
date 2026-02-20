# RLAPP — Testing Guide

**Estrategia de testing, cobertura, violaciones y desafíos.**

## ✅ Actualización 2026-02-19 (flujo operativo completo)

Se agregó cobertura explícita para las features nuevas del flujo clínico por rol:

- Taquilla obligatoria con estados alternos (`PagoPendiente`, `AusenteTaquilla`, `CanceladoPorPago`)
- Consulta con ausencia y cancelación por ausencia (`AusenteConsulta`, `CanceladoPorAusencia`)
- Gestión de consultorios activos/inactivos para `medical/call-next`
- Prevención de doble registro activo (reintentos de registro duplicado)

Archivo principal de cobertura de flujo:

- `src/Tests/WaitingRoom.Tests.Domain/Aggregates/WaitingQueueAttentionFlowTests.cs`

Matriz mínima de casos (al menos 1 test por caso):

- Registro inicial de paciente
- Llamado a taquilla
- Pago validado
- Pago pendiente
- Ausencia en taquilla
- Cancelación por pago fallido
- Llamado a consulta (claim)
- Inicio de consulta
- Finalización de consulta
- Ausencia en consulta con segundo intento cancelado
- Consultorio inactivo bloquea llamado
- Activación/desactivación de consultorio

Cobertura específica de duplicados en registro:

- `CheckInPatient_SamePatient_MoreThanTwoAttempts_ThrowsOnSecondAndThirdAttempt`

---

## 🎯 Estrategia de Testing

### Pirámide de Testing

```
         /\
        /  \        🔺 E2E Tests
       /────\       System-wide behavior
      /      \      (Docker + RabbitMQ required)
     /        \     ⏱ ~5-30 segundos
    /──────────\
   /            \   🟡 Integration Tests
  /              \  Database + components
 /────────────────\ ⏱ ~1-10 segundos
/                  \
/                  \
──────────────────── 🟢 Unit Tests
      Domain      Pure logic, zero I/O
   Value Objects ⏱ ~10-100 ms
     Aggregates  (Mayoría de tests)
```

### Estrategia por Capa

| Capa | Strategy | Tools | Coverage |
|------|----------|-------|----------|
| **Domain** | Pure unit tests (no mocks) | XUnit | 95%+ |
| **Application** | Mock infrastructure | Moq | 80%+ |
| **Infrastructure** | Integration with DB | XUnit + Docker | 70%+ |
| **API** | Minimal (tested via integration) | - | 60%+ |

---

## 🟢 Domain Tests

### Archivo: `WaitingRoom.Tests.Domain/Aggregates/WaitingQueueTests.cs`

**Enfoque:** Test pure domain logic sin dependencias.

#### Test 1: WaitingQueue.Create()

```csharp
[Fact]
public void Create_WithValidData_CreatesQueue()
{
    // ARRANGE
    var metadata = EventMetadata.CreateNew("QUEUE-01", "system");

    // ACT
    var queue = WaitingQueue.Create(
        queueId: "QUEUE-01",
        queueName: "Main Reception",
        maxCapacity: 10,
        metadata: metadata);

    // ASSERT
    queue.Id.Should().Be("QUEUE-01");
    queue.QueueName.Should().Be("Main Reception");
    queue.MaxCapacity.Should().Be(10);
    queue.CurrentCount.Should().Be(0);
    queue.Version.Should().Be(1);
    queue.UncommittedEvents.Should().HaveCount(1);
    queue.UncommittedEvents[0].Should().BeOfType<WaitingQueueCreated>();
}
```

**Lo que valida:**

- Factory method crea agregado válido
- Evento es emitido correctamente
- Estado inicial es correcto

#### Test 2: Violación de Invariante

```csharp
[Fact]
public void Create_WithInvalidQueueName_ThrowsDomainException()
{
    // ARRANGE
    var metadata = EventMetadata.CreateNew("QUEUE-01", "system");

    // ACT & ASSERT
    Assert.Throws<DomainException>(() =>
        WaitingQueue.Create(
            queueId: "QUEUE-01",
            queueName: "",  // ← Inválido
            maxCapacity: 10,
            metadata: metadata));
}
```

**Lo que valida:**

- Invariante se enforza
- Excepción correcta es lanzada
- No hay evento si validación falla

#### Test 3: CheckInPatient - Happy Path

```csharp
[Fact]
public void CheckInPatient_WithValidData_EmitsPatientCheckedInEvent()
{
    // ARRANGE
    var queue = CreateQueue();  // Helper: queue con 0 pacientes

    // ACT
    queue.CheckInPatient(
        patientId: PatientId.Create("PAT-001"),
        patientName: "John Doe",
        priority: Priority.Create(Priority.High),
        consultationType: ConsultationType.Create("General"),
        checkInTime: DateTime.UtcNow,
        metadata: EventMetadata.CreateNew(queue.Id, "nurse"));

    // ASSERT
    queue.CurrentCount.Should().Be(1);
    queue.UncommittedEvents.Should().HaveCount(1);
    queue.UncommittedEvents[0].Should().BeOfType<PatientCheckedIn>();

    var evt = (PatientCheckedIn)queue.UncommittedEvents[0];
    evt.PatientId.Should().Be("PAT-001");
    evt.Priority.Should().Be(Priority.High);
}
```

#### Test 4: Violación - Capacidad

```csharp
[Fact]
public void CheckInPatient_AtCapacity_ThrowsDomainException()
{
    // ARRANGE
    var queue = CreateQueue(maxCapacity: 1);  // Capacidad = 1

    // Check in 1 paciente (llena la cola)
    var metadata1 = EventMetadata.CreateNew(queue.Id, "nurse");
    queue.CheckInPatient(
        PatientId.Create("PAT-001"),
        "Patient 1",
        Priority.Create(Priority.Low),
        ConsultationType.Create("General"),
        DateTime.UtcNow,
        metadata1);

    // ACT & ASSERT - Segundo paciente falla
    var metadata2 = EventMetadata.CreateNew(queue.Id, "nurse");
    Assert.Throws<DomainException>(() =>
        queue.CheckInPatient(
            PatientId.Create("PAT-002"),
            "Patient 2",
            Priority.Create(Priority.Low),
            ConsultationType.Create("General"),
            DateTime.UtcNow,
            metadata2));
}
```

#### Test 5: Violación - Duplicado

```csharp
[Fact]
public void CheckInPatient_DuplicatePatient_ThrowsDomainException()
{
    // ARRANGE
    var queue = CreateQueue();
    var patientId = PatientId.Create("PAT-001");

    // Primera entrada
    var metadata1 = EventMetadata.CreateNew(queue.Id, "nurse");
    queue.CheckInPatient(
        patientId, "John Doe", Priority.Create(Priority.Low),
        ConsultationType.Create("General"), DateTime.UtcNow, metadata1);

    // ACT & ASSERT - Duplicado falla
    var metadata2 = EventMetadata.CreateNew(queue.Id, "nurse");
    Assert.Throws<DomainException>(() =>
        queue.CheckInPatient(
            patientId, "John Doe", Priority.Create(Priority.Low),
            ConsultationType.Create("General"), DateTime.UtcNow, metadata2));
}
```

### Value Object Tests

**Archivo:** `WaitingRoom.Tests.Domain/ValueObjects/PriorityTests.cs`

```csharp
[Fact]
public void Create_WithValid Priority_Succeeds()
{
    var priority = Priority.Create("High");
    priority.Value.Should().Be("High");
    priority.Level.Should().Be(3);
}

[Fact]
public void Create_WithNormalized_Input()
{
    // Case-insensitive input
    var priority = Priority.Create("high");  // lowercase
    priority.Value.Should().Be("High");      // normalized to canonical
}

[Fact]
public void Create_WithInvalid_ThrowsDomainException()
{
    Assert.Throws<DomainException>(() =>
        Priority.Create("CRITICAL"));  // Not in whitelist
}
```

### Coverage Metrics (Domain)

```
✓ Aggregate.Create()              - 100%
✓ Aggregate.CheckInPatient()      - 100%
✓ Invariants validation           - 100%
✓ Event handlers (When methods)   - 100%
✓ Value Objects creation          - 100%
✓ Entity construction             - 100%
─────────────────────────────────
TOTAL Domain Coverage:            ~95%
```

---

## 🟡 Application Tests

### Archivo: `WaitingRoom.Tests.Application/CommandHandlers/CheckInPatientCommandHandlerTests.cs`

**Enfoque:** Test orchestration logic con mocks de infraestructura.

#### Test 1: Happy Path

```csharp
[Fact]
public async Task HandleAsync_ValidCommand_SavesAndPublishesEvents()
{
    // ARRANGE
    var command = new CheckInPatientCommand
    {
        QueueId = "QUEUE-01",
        PatientId = "PAT-001",
        PatientName = "John Doe",
        Priority = Priority.High,
        ConsultationType = "General",
        Actor = "nurse-001"
    };

    // Create aggregate with one event
    var metadata = EventMetadata.CreateNew("QUEUE-01", "system");
    var queue = WaitingQueue.Create("QUEUE-01", "Main Queue", 10, metadata);
    queue.ClearUncommittedEvents();

    // Mock dependencies
    var eventStoreMock = new Mock<IEventStore>();
    var publisherMock = new Mock<IEventPublisher>();

    eventStoreMock
        .Setup(es => es.LoadAsync("QUEUE-01", It.IsAny<CancellationToken>()))
        .ReturnsAsync(queue);

    var clock = new FakeClock();
    var handler = new CheckInPatientCommandHandler(
        eventStoreMock.Object,
        publisherMock.Object,
        clock);

    // ACT
    var result = await handler.HandleAsync(command);

    // ASSERT
    result.Should().BeGreaterThan(0);

    eventStoreMock.Verify(
        es => es.SaveAsync(It.IsAny<WaitingQueue>(), It.IsAny<CancellationToken>()),
        Times.Once);

    publisherMock.Verify(
        pub => pub.PublishAsync(
            It.IsAny<IEnumerable<DomainEvent>>(),
            It.IsAny<CancellationToken>()),
        Times.Once);
}
```

#### Test 2: Aggregate Not Found

```csharp
[Fact]
public async Task HandleAsync_QueueNotFound_ThrowsAggregateNotFoundException()
{
    // ARRANGE
    var command = new CheckInPatientCommand
    {
        QueueId = "QUEUE-NOTFOUND",
        PatientId = "PAT-001",
        PatientName: "John",
        Priority: "High",
        ConsultationType: "General",
        Actor: "nurse-001"
    };

    var eventStoreMock = new Mock<IEventStore>();
    eventStoreMock
        .Setup(es => es.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((WaitingQueue)null);  // ← Not found

    var handler = new CheckInPatientCommandHandler(...);

    // ACT & ASSERT
    await Assert.ThrowsAsync<AggregateNotFoundException>(
        () => handler.HandleAsync(command));
}
```

#### Test 3: Domain Validation Bubbles

```csharp
[Fact]
public async Task HandleAsync_DomainViolation_PropagatesDomainException()
{
    // ARRANGE
    var command = new CheckInPatientCommand
    {
        QueueId = "QUEUE-01",
        PatientId: "PAT-001",
        PatientName: "John",
        Priority: "INVALID",  // ← Invalid priority
        ConsultationType: "General",
        Actor: "nurse-001"
    };

    var queue = CreateQueueWithMaxCapacity(10);

    var eventStoreMock = new Mock<IEventStore>();
    eventStoreMock
        .Setup(es => es.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(queue);

    var handler = new CheckInPatientCommandHandler(...);

    // ACT & ASSERT
    await Assert.ThrowsAsync<DomainException>(
        () => handler.HandleAsync(command));
}
```

### Coverage Metrics (Application)

```
✓ Command handler load → 100%
✓ Domain logic invocation → 90%
✓ EventStore.SaveAsync call → 100%
✓ Publishing → 100%
✓ Exception bubbling → 90%
─────────────────────────────────
TOTAL Application Coverage:       ~85%
```

---

## 🔵 Integration Tests

### Archivo: `WaitingRoom.Tests.Integration/EndToEnd/EventDrivenPipelineE2ETests.cs`

**Requisito:** Docker running (PostgreSQL + RabbitMQ)

```csharp
[Fact]
public async Task E2E_PatientCheckIn_UpdatesProjection()
{
    // ARRANGE
    using var context = await CreateTestContext();  // Docker containers

    // Create queue
    var queueId = "TEST-QUEUE-" + Guid.NewGuid().ToString();
    var processor = context.ProcessorFactory.Create<WaitingQueueProcessor>();

    var createCommand = new CreateWaitingQueueCommand
    {
        QueueId = queueId,
        QueueName = "Test Queue",
        MaxCapacity = 20
    };

    await processor.ProcessAsync(createCommand);

    // ACT - Check in patient
    var checkInCommand = new CheckInPatientCommand
    {
        QueueId = queueId,
        PatientId = "PAT-E2E-001",
        PatientName: "E2E Patient",
        Priority: "High",
        ConsultationType: "General",
        Actor: "test-user"
    };

    await processor.ProcessAsync(checkInCommand);

    // Wait for async processing
    await Task.Delay(2000);

    // ASSERT - Projection updated
    var projection = await context.ProjectionProvider
        .GetMonitorViewAsync(queueId);

    projection.Should().NotBeNull();
    projection.TotalPatients.Should().Be(1);
    projection.HighPriorityCount.Should().Be(1);
}
```

---

## 🚫 Testabilidad - Violaciones

### Problema 1: Reflection-Based Event Dispatch

**En:** `AggregateRoot.ApplyEvent()`

```csharp
var whenMethod = GetType()
    .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
    .FirstOrDefault(m =>
        m.Name == "When" &&
        m.GetParameters()[0].ParameterType == @event.GetType());
```

**Impacto:**

- Tests de domain pueden fallar si método `When` renombrado
- Reflection es más lento (pero aceptable para events)
- Errores vistos en runtime, no compile time

**Mitigación:**

- Convention-based (nombre siempre "When")
- Unit tests validan event dispatch
- Difícil refactorizar sin romper tests

### Problema 2: ValueObject Creation en Handler

**En:** `CheckInPatientCommandHandler.HandleAsync()`

```csharp
var patientId = PatientId.Create(command.PatientId);
var priority = Priority.Create(command.Priority);
// ... múltiples creaciones
```

**Impacto:**

- Handler tiene lógica de construcción (mejorable)
- Violación de Single Responsibility

**Mitigación:**

- ValueObject validation está en dominio
- Aplicación solo orquesta
- Aceptable en handler actual (pequeño)

### Problema 3: No Pure Testable Sin Docker (Integration)

**En:** `PostgresEventStore`, `PostgresOutboxStore`

```csharp
// Cannot test without DB
public async Task<WaitingQueue?> LoadAsync(string aggregateId, ...)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    // ... SQL execution
}
```

**Impacto:**

- Tests de infraestructura requieren Docker
- No hay in-memory stub por defecto

**Mitigación:**

- En tests: use `InMemoryEventStore` (proyecto de tests)
- Docker Compose hace setup fácil
- CI/CD puede usar contenedores

---

## 📊 Cobertura Actual

```
┌─────────────┬──────────┬─────────────────────────────┐
│ Capa        │ Coverage │ Método                      │
├─────────────┼──────────┼─────────────────────────────┤
│ Domain      │  95%     │ Unit tests (0 mocks)        │
│ Application │  85%     │ Unit + Mock tests           │
│ Integration │  70%     │ Docker + DB (limited)       │
│ API         │  50%     │ Via integration (limited)   │
│ Worker      │  60%     │ Background job tests        │
│ Projections │  75%     │ Handler tests (mocked ctx)  │
├─────────────┼──────────┼─────────────────────────────┤
│ TOTAL       │  ~75%    │ Mixed strategy              │
└─────────────┴──────────┴─────────────────────────────┘
```

---

## 🎯 Recomendaciones de Testing

### Prioridades

1. **Alto:** Domain tests (95%+ coverage)
   - Reglas de negocio son críticas
   - Fast, deterministic

2. **Medio:** Application tests (80%+ coverage)
   - Flujo end-to-end importante
   - Mocks hacen tests rápidos

3. **Medio:** Integration tests
   - Validar persistencia
   - Validar messaging (con Docker)

4. **Bajo:** API tests
   - Middleware es thin
   - Infrastructure testing es más importante

### Buenas Prácticas

1. **Tests tienen el mismo nivel de calidad que código**

   ```csharp
   // ✗ BAD: Vago
   Assert.True(result);

   // ✓ GOOD: Explícito
   queue.CurrentCount.Should().Be(1);
   queue.UncommittedEvents.Should().HaveCount(1);
   ```

2. **AAA Pattern**

   ```csharp
   // Arrange - Setup
   var queue = CreateQueue();

   // Act - Execute
   queue.CheckInPatient(...);

   // Assert - Verify
   queue.CurrentCount.Should().Be(1);
   ```

3. **Test nombres describen el comportamiento**

   ```csharp
   // ✗ BAD: Vago
   [Fact] public void Test1() { }

   // ✓ GOOD: Claro
   [Fact]
   public void CheckInPatient_AtCapacity_ThrowsDomainException() { }
   ```

4. **Sin lógica compleja en tests**

   ```csharp
   // ✗ BAD: Lógica compleja
   foreach (var patient in patientList) {
       var result = CheckIn(patient);
       if (result != null) Assert.True(...);
   }

   // ✓ GOOD: Directo
   queue.CheckInPatient(...);
   queue.CurrentCount.Should().Be(1);
   ```

---

## 🚀 Ejecución de Tests

```bash
# Todos los tests
bash run-complete-test.sh

# Domain solamente
dotnet test src/Tests/WaitingRoom.Tests.Domain

# Application solamente
dotnet test src/Tests/WaitingRoom.Tests.Application

# Integration (requiere Docker)
dotnet test src/Tests/WaitingRoom.Tests.Integration

# Con output detallado
dotnet test --logger "console;verbosity=detailed"

# Coverage (si está disponible)
dotnet test /p:CollectCoverage=true
```

---

**Última actualización:** Febrero 2026
