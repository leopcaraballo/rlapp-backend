# RLAPP — WaitingRoom Backend

**Una arquitectura hexagonal basada en event sourcing para sistemas de gestión de colas de espera.**

---

## 📋 Descripción General

RLAPP es un **backend de microservicios event-driven** construido en .NET 10 que implementa un servicio de gestión de colas de espera para atención sanitaria. El sistema proporciona:

- **Event Sourcing** como patrón principal de persistencia
- **CQRS** con separación completa entre escribir (commands) y leer (queries)
- **Outbox Pattern** para garantizar entrega confiable de eventos
- **Proyecciones** (read models) para queries rápidas y optimizadas
- **Arquitectura Hexagonal** para máximo desacoplamiento
- **Observabilidad** completa con métricas, trazas y lag tracking

### Problema que Resuelve

Un hospital necesita gestionar colas de espera con:

- Pacientes con diferentes prioridades
- Validación de capacidad máxima
- Trazabilidad de eventos
- Queries rápidas de estado de colas
- Resiliencia ante fallos

**Solución:** Arquitectura event-driven donde cada interacción del paciente es un evento inmutable que reconstruye el estado actual del sistema.

---

## 🏗️ Arquitectura

**Patrón: Hexagonal (Ports & Adapters) + Event Sourcing + CQRS**

```
┌─────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                    │
│              (API Endpoints, Middleware)                 │
└──────────────────┬──────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────┐
│                 APPLICATION LAYER                        │
│        (Command Handlers, Orchestration)                │
│     ✗ NO business logic here                            │
│     ✓ Pure orchestration                                │
└──────────────────┬──────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────┐
│                   DOMAIN LAYER (CORE)                    │
│         (Aggregates, Events, Value Objects)             │
│     ✓ ALL business rules here                           │
│     ✓ Zero external dependencies                        │
│     ✓ Pure .NET - no EF, no DB, no HTTP                │
└──────────────────┬──────────────────────────────────────┘
                   │
┌──────────────────▼──────────────────────────────────────┐
│              INFRASTRUCTURE LAYER                        │
│   (EventStore, Outbox, RabbitMQ, Projections)          │
│     ✓ Concrete implementations                          │
│     ✓ Database schemas                                  │
│     ✓ Message broker integration                        │
└─────────────────────────────────────────────────────────┘
```

**Principios arquitectónicos:**

- Domain tiene cero dependencias
- Application NO tiene lógica de negocio (solo orquestación)
- Infrastructure es completamente intercambiable
- Presentation es un puro adaptador HTTP

---

## 🛠️ Stack Tecnológico

| Componente | Tecnología | Versión | Propósito |
|-----------|-----------|---------|----------|
| **Runtime** | .NET | 10.0 | Framework base |
| **API** | ASP.NET Core Minimal APIs | 10.0 | Endpoints HTTP |
| **BD (Write)** | PostgreSQL | 16 | Event Store (JSONB) |
| **BD (Read)** | PostgreSQL | 16 | Proyecciones (In-Memory en tests) |
| **Message Broker** | RabbitMQ | 3.12 | Distribución de eventos |
| **Serialización** | Newtonsoft.Json | 13.0.3 | JSON + Events |
| **Data Access** | Dapper | 2.1.35 | Queries eficientes |
| **Testing** | XUnit + Moq | Latest | Unit + Integration tests |
| **Observabilidad** | Prometheus + Grafana | Latest | Métricas y dashboards |
| **Logging** | Serilog | Latest | Structured logging |

---

## 📦 Estructura de Carpetas

```
rlapp-backend/
├── src/
│   ├── BuildingBlocks/              # Bloques reutilizables
│   │   ├── BuildingBlocks.EventSourcing/   # AggregateRoot, DomainEvent
│   │   ├── BuildingBlocks.Messaging/       # IEventSerializer
│   │   └── BuildingBlocks.Observability/   # EventLagTracker
│   │
│   ├── Services/
│   │   └── WaitingRoom/             # Bounded Context principal
│   │       ├── WaitingRoom.Domain/          # ✓ Lógica de negocio pura
│   │       ├── WaitingRoom.Application/     # ✓ Orquestación
│   │       ├── WaitingRoom.Infrastructure/  # ✓ Persistencia + Mensajería
│   │       ├── WaitingRoom.API/             # ✓ Endpoints HTTP
│   │       ├── WaitingRoom.Projections/     # ✓ Read Models
│   │       └── WaitingRoom.Worker/          # ✓ Background Job
│   │
│   └── Tests/
│       ├── WaitingRoom.Tests.Domain/        # Unit tests agregados
│       ├── WaitingRoom.Tests.Application/   # Unit tests handlers
│       ├── WaitingRoom.Tests.Integration/   # Integration tests (DB + RabbitMQ)
│       └── WaitingRoom.Tests.Projections/   # Projection tests
│
├── infrastructure/                  # Docker composition files
│   ├── postgres/                    # Init scripts BD
│   ├── rabbitmq/                    # RabbitMQ config
│   ├── prometheus/                  # Métricas scraping
│   └── grafana/                     # Dashboards
│
├── docker-compose.yml               # Orquestación local
├── RLAPP.slnx                       # Solución (.NET 10)
├── README.md                        # Este archivo
├── ARCHITECTURE.md                  # Diagrama y decisiones
├── DOMAIN_OVERVIEW.md               # Entidades y reglas
├── APPLICATION_FLOW.md              # Casos de uso paso a paso
├── INFRASTRUCTURE.md                # Implementaciones
├── TESTING_GUIDE.md                 # Estrategia de testing
└── AUDIT_REPORT.md                  # Evaluación técnica
```

---

## 🚀 Requisitos

### Local Development

- **.NET 10 SDK** (o superior)
- **Docker + Docker Compose** (para PostgreSQL, RabbitMQ, Prometheus, Grafana)
- **Git**

### Runtime

- **PostgreSQL 16+** (Event Store + Read Models)
- **RabbitMQ 3.12+** (Message Broker)
- **Prometheus** (Métricas)
- **Grafana** (Dashboards)

---

## 🏃 Cómo Ejecutar

### 1. Clonar el Repositorio

```bash
git clone <repo-url>
cd rlapp-backend
```

### 2. Configurar Variables de Entorno

```bash
cp .env.template .env
# Editar .env si es necesario
```

### 3. Iniciar Infraestructura (Docker Compose)

```bash
docker-compose up -d

# Verificar que servicios estén saludables
docker-compose ps

# Logs en tiempo real
docker-compose logs -f
```

**Servicios disponibles:**

- PostgreSQL: `localhost:5432`
- RabbitMQ Management: `http://localhost:15672` (guest/guest)
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000` (admin/admin)

### 4. Restaurar Dependencias

```bash
dotnet restore
```

### 5. Ejecutar API

```bash
cd src/Services/WaitingRoom/WaitingRoom.API
dotnet run

# La API estará disponible en http://localhost:5000
```

### 6. Ejecutar Worker (en otra terminal)

```bash
cd src/Services/WaitingRoom/WaitingRoom.Worker
dotnet run

# Procesa eventos del Outbox a RabbitMQ
```

---

## 🧪 Ejecutar Tests

### Tests Unitarios (Domain)

```bash
dotnet test src/Tests/WaitingRoom.Tests.Domain

# Específicamente un test
dotnet test src/Tests/WaitingRoom.Tests.Domain -k "Create_WithValidData"
```

### Tests de Aplicación

```bash
dotnet test src/Tests/WaitingRoom.Tests.Application
```

### Tests de Integración

```bash
# Requiere Docker running
dotnet test src/Tests/WaitingRoom.Tests.Integration

# Con output detallado
dotnet test src/Tests/WaitingRoom.Tests.Integration -v detailed
```

### Tests de Proyecciones

```bash
dotnet test src/Tests/WaitingRoom.Tests.Projections
```

### Ejecutar Todos los Tests

```bash
bash run-complete-test.sh
```

---

## 📝 Variables de Entorno Principales

| Variable | Descripción | Ejemplo |
|----------|-------------|---------|
| `EventStore__ConnectionString` | Conexión a BD de eventos | `Host=postgres;...` |
| `RabbitMq__HostName` | Host del broker | `localhost` |
| `RabbitMq__Port` | Puerto RabbitMQ | `5672` |
| `OutboxDispatcher__PollingIntervalSeconds` | Polling del outbox worker | `5` |
| `OutboxDispatcher__BatchSize` | Eventos por batch | `100` |
| `ASPNETCORE_ENVIRONMENT` | Ambiente | `Development` |
| `Logging__LogLevel__Default` | Nivel de logs | `Information` |

Ver [.env.template](.env.template) para lista completa.

---

## 💡 Conceptos Clave

### Event Sourcing

El estado del sistema se reconstruye desde una secuencia inmutable de eventos. La "fuente de verdad" es el log de eventos, no el estado actual.

```
Command → Aggregate (aplica reglas) → Evento → EventStore → Proyecciones
```

### CQRS (Command Query Responsibility Segregation)

- **Write Model (Commands):** Colas de espera con validaciones
- **Read Model (Queries):** Vistas optimizadas para consultas rápidas

### Outbox Pattern

Los eventos se persisten en la misma transacción que el comando, en una tabla `outbox`. Un worker los consume y publica a RabbitMQ en segundo plano, garantizando entrega confiable.

```
Command → EventStore + Outbox (transacción única)
              ↓
         OutboxWorker (async)
              ↓
         RabbitMQ (publicación idempotente)
              ↓
         Proyecciones
```

### Hexagonal Architecture

Las dependencias externas (DB, mensajería, HTTP) son inyectadas en la infraestructura. El dominio nunca conoce estas dependencias.

---

## 🔗 Endpoints API

### POST /api/waiting-room/check-in

Registra la entrada de un paciente a la cola de espera.

**Request:**

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "patientName": "Juan Pérez",
  "priority": "High",
  "consultationType": "General",
  "actor": "nurse-001",
  "notes": "Dolor de cabeza"
}
```

**Response (200):**

```json
{
  "success": true,
  "message": "Patient checked in successfully",
  "correlationId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "eventCount": 1
}
```

**Errores:**

- `400` - Violación de regla de negocio (cola llena, paciente duplicado)
- `404` - Cola no encontrada
- `409` - Conflicto de versión (modificación concurrente)
- `500` - Error del servidor

### GET /api/v1/waiting-room/{queueId}/monitor

Obtiene métricas KPI de la cola.

### GET /api/v1/waiting-room/{queueId}/queue-state

Obtiene estado detallado de la cola con lista de pacientes.

### POST /api/v1/waiting-room/{queueId}/rebuild

Inicia reconstrucción de proyecciones desde el event store.

---

## 📊 Monitoreo y Observabilidad

### Métricas (Prometheus)

- `event_sourcing_lag_ms` - Lag entre evento y proyección
- `outbox_dispatch_duration_ms` - Tiempo de dispatching
- `queue_current_capacity` - Ocupación actual
- `queue_checkins_total` - Total de check-ins

**Scrape desde:** `http://localhost:9090`

### Dashboards (Grafana)

**URL:** `http://localhost:3000`

**Credenciales:** `admin / admin`

**Dashboards preconfigurados:**

- Event Processing Lag
- Infrastructure Health
- Queue Metrics

### Logs Estructurados (Serilog)

Todos los logs incluyen `CorrelationId` para trazabilidad distribuida.

```csharp
logger.LogInformation(
    "CheckIn completed. CorrelationId: {CorrelationId}, EventCount: {EventCount}",
    correlationId,
    eventCount);
```

---

## 🛡️ Riesgos Conocidos y Mitigación

| Riesgo | Severidad | Mitigación |
|--------|-----------|-----------|
| **Lag de Proyecciones** | Medium | Monitoreo activo en Grafana + alertas |
| **Fallo de RabbitMQ** | Medium | Outbox pattern garantiza no perder eventos |
| **Inconsistencia DB** | Low | Event sourcing como SSOT (Single Source of Truth) |
| **Mensajes Duplicados** | Low | Idempotency keys + handlers idempotentes |
| **Fallo de Dispatch** | Low | Reintentos con backoff exponencial |

---

## 🚦 Roadmap Técnico Sugerido

### Fase 1 (Actual)

- [x] Event Sourcing básico
- [x] CQRS con Outbox Pattern
- [x] Proyecciones In-Memory
- [x] Tests unitarios domain
- [x] Observabilidad básica

### Fase 2 (Próxima)

- [ ] Proyecciones persistentes en PostgreSQL
- [ ] Event versioning / schema evolution
- [ ] Snapshot pattern para agregados grandes
- [ ] Saga pattern para procesos multi-agregado
- [ ] Rate limiting y circuit breaker

### Fase 3

- [ ] Sagas distribuidas entre bounded contexts
- [ ] Dead letter queues
- [ ] Event replay desde punto específico
- [ ] Audit trail integrado
- [ ] Compliance con GDPR (derecho al olvido)

---

## 📚 Documentación Relacionada

- [**ARCHITECTURE.md**](ARCHITECTURE.md) - Decisiones arquitectónicas y patrones
- [**DOMAIN_OVERVIEW.md**](DOMAIN_OVERVIEW.md) - Entidades, agregados, reglas de negocio
- [**APPLICATION_FLOW.md**](APPLICATION_FLOW.md) - Flujo de ejecución paso a paso
- [**INFRASTRUCTURE.md**](INFRASTRUCTURE.md) - Implementaciones concretas
- [**TESTING_GUIDE.md**](TESTING_GUIDE.md) - Estrategia y cobertura
- [**AUDIT_REPORT.md**](AUDIT_REPORT.md) - Evaluación técnica externa

---

## 🤝 Contribuyendo

1. Crear rama: `git checkout -b feature/my-feature`
2. Commit con mensaje descriptivo: `git commit -m "feat(domain): add patient removal"`
3. Push: `git push origin feature/my-feature`
4. Crear Pull Request
5. Asegurar tests pasen: `bash run-complete-test.sh`

---

## 📞 Soporte

Para preguntas técnicas sobre la arquitectura, consultar la [Auditoría Técnica](AUDIT_REPORT.md).

---

**Última actualización:** Febrero 2026

**Mantainer:** Architecture Team

**Licencia:** MIT
