# 🏗️ C4 Architecture Diagrams — RLAPP Backend

Architectural diagrams following the C4 model (Context, Container, Component, Code).

**Documentation Standard:** C4 Model by Simon Brown
**Notation:** Mermaid diagrams
**Last Updated:** 2026-02-19

> Runtime note: command and query endpoints shown are part of the current public contract. Canonical API details are documented in [../API.md](../API.md).

---

## 📋 Table of Contents

1. [Level 1: System Context](#level-1-system-context)
2. [Level 2: Container](#level-2-container)
3. [Level 3: Component (WaitingRoom)](#level-3-component-waitingroom)
4. [Supplementary Diagrams](#supplementary-diagrams)
   - [Event Sourcing Flow](#event-sourcing-flow)
   - [CQRS Flow](#cqrs-flow)
   - [Outbox Pattern](#outbox-pattern)
   - [Deployment View](#deployment-view)

---

## Level 1: System Context

**Scope:** Entire RLAPP system
**Primary Elements:** People, external systems
**Audience:** Everyone (executives, developers, end users)

### Diagram

```mermaid
graph TB
    subgraph "Healthcare Organization"
        Reception["👤 Reception Staff<br/>(Healthcare Staff)"]
        Cashier["👤 Cashier Staff<br/>(Healthcare Staff)"]
        Doctor["👤 Doctor<br/>(Healthcare Staff)"]
        Admin["👤 Administrator<br/>(System Admin)"]
    end

    subgraph "RLAPP System"
        RLAPP["RLAPP Backend<br/>(Event-Sourced WaitingRoom Service)<br/>Manages patient queue, cashier flow, and consultation workflow"]
    end

    subgraph "External Systems"
        EHR["Electronic Health Records<br/>(External System)<br/>Patient history & medical records"]
        Monitoring["Monitoring System<br/>(Grafana + Prometheus)<br/>Observability & alerting"]
    end

    Reception -->|"Register patients<br/>View queue status"| RLAPP
    Cashier -->|"Call next at cashier<br/>Validate payment"| RLAPP
    Doctor -->|"Activate consulting room<br/>Call/start/finish consultation"| RLAPP
    Admin -->|"Monitor system health<br/>Manage queues"| RLAPP

    RLAPP -.->|"Read patient data<br/>(Future integration)"| EHR
    RLAPP -->|"Publish metrics<br/>Send logs"| Monitoring

    style RLAPP fill:#1168bd,stroke:#0b4884,color:#ffffff
    style EHR fill:#999999,stroke:#666666,color:#ffffff
    style Monitoring fill:#999999,stroke:#666666,color:#ffffff
```

### Key Relationships

| Actor/System | Interacts With | Purpose |
|--------------|----------------|---------|
| **Reception Staff** | RLAPP Backend | Register patients, view queue status |
| **Cashier Staff** | RLAPP Backend | Call next at cashier, validate payment, manage payment exceptions |
| **Doctor** | RLAPP Backend | Activate consulting room, call/start/finish consultation |
| **Administrator** | RLAPP Backend | Monitor system health, configure queues |
| **RLAPP Backend** | EHR (Future) | Retrieve patient medical history |
| **RLAPP Backend** | Monitoring | Send metrics, logs, alerts |

---

## Level 2: Container

**Scope:** RLAPP Backend system
**Primary Elements:** Applications, data stores, communication protocols
**Audience:** Technical stakeholders, architects, developers

### Diagram

```mermaid
graph TB
    subgraph "Users"
        User["👤 Healthcare Staff<br/>(Reception, Cashier, Doctor)"]
    end

    subgraph "RLAPP Backend System"
        API["WaitingRoom API<br/>(ASP.NET Core 10)<br/>REST endpoints<br/>Port: 5000"]
        Worker["Outbox Worker<br/>(.NET Background Service)<br/>Publishes events from Outbox"]

        subgraph "Domain Layer"
            Domain["WaitingRoom Domain<br/>(Pure C# Library)<br/>Aggregates, Events, Rules"]
        end

        subgraph "Application Layer"
            Application["Application Services<br/>(C# Library)<br/>Command Handlers, Ports"]
        end

        subgraph "Infrastructure Layer"
            EventStoreImpl["PostgresEventStore<br/>(Npgsql + Dapper)<br/>Event persistence"]
            PublisherImpl["RabbitMqEventPublisher<br/>(RabbitMQ.Client)<br/>Event distribution"]
            ProjectionImpl["Projection Handlers<br/>(C# Library)<br/>Update read models"]
        end
    end

    subgraph "Data Stores"
        PostgreSQL["PostgreSQL<br/>(Database)<br/>EventStore + Outbox + ReadModels<br/>Port: 5432"]
        RabbitMQ["RabbitMQ<br/>(Message Broker)<br/>Event distribution<br/>Port: 5672"]
    end

    subgraph "Monitoring"
        Prometheus["Prometheus<br/>(Metrics)<br/>Port: 9090"]
        Grafana["Grafana<br/>(Dashboards)<br/>Port: 3000"]
    end

    User -->|"HTTP REST<br/>JSON"| API

    API --> Application
    Application --> Domain
    Application --> EventStoreImpl
    Application --> PublisherImpl

    EventStoreImpl -->|"SQL + JSONB"| PostgreSQL
    Worker -->|"Poll"| PostgreSQL
    Worker -->|"AMQP"| RabbitMQ

    RabbitMQ -->|"Subscribe"| ProjectionImpl
    ProjectionImpl -->|"UPDATE"| PostgreSQL

    API -->|"Read"| PostgreSQL

    API -->|"Metrics"| Prometheus
    Worker -->|"Metrics"| Prometheus
    Prometheus -->|"Scrape"| Grafana

    style API fill:#1168bd,stroke:#0b4884,color:#ffffff
    style Worker fill:#1168bd,stroke:#0b4884,color:#ffffff
    style Domain fill:#438dd5,stroke:#2e6295,color:#ffffff
    style Application fill:#438dd5,stroke:#2e6295,color:#ffffff
    style EventStoreImpl fill:#85bbf0,stroke:#5d91c9,color:#000000
    style PublisherImpl fill:#85bbf0,stroke:#5d91c9,color:#000000
    style ProjectionImpl fill:#85bbf0,stroke:#5d91c9,color:#000000
    style PostgreSQL fill:#2d9c5e,stroke:#1f6b41,color:#ffffff
    style RabbitMQ fill:#ff6600,stroke:#cc5200,color:#ffffff
    style Prometheus fill:#e6522c,stroke:#b33f1e,color:#ffffff
    style Grafana fill:#f46800,stroke:#c45300,color:#ffffff
```

### Container Descriptions

| Container | Technology | Responsibility | Scaling |
|-----------|-----------|----------------|---------|
| **WaitingRoom API** | ASP.NET Core 10 (Minimal APIs) | HTTP endpoints, command orchestration, query handlers | Horizontal (stateless) |
| **Outbox Worker** | .NET Background Service | Poll Outbox table, publish to RabbitMQ | Single instance (leader election for HA) |
| **Domain** | Pure C# (.NET 10) | Business logic, aggregates, domain events | N/A (library) |
| **Application** | C# (.NET 10) | Use cases, command handlers, ports (interfaces) | N/A (library) |
| **PostgresEventStore** | Npgsql + Dapper | Event Store implementation (IEventStore port) | N/A (library) |
| **RabbitMqEventPublisher** | RabbitMQ.Client | Event publisher implementation (IEventPublisher port) | N/A (library) |
| **Projection Handlers** | C# (.NET 10) | Subscribe to events, update read models | Horizontal (consumer groups) |
| **PostgreSQL** | PostgreSQL 16 | Event Store, Outbox, Read Models (3 schemas) | Vertical (future: read replicas) |
| **RabbitMQ** | RabbitMQ 3.12 | Message broker, topic exchange | Cluster (3+ nodes for HA) |
| **Prometheus** | Prometheus | Metrics collection, alerting | Single instance |
| **Grafana** | Grafana | Dashboards, visualization | Single instance |

---

## Level 3: Component (WaitingRoom)

**Scope:** WaitingRoom API container internals
**Primary Elements:** Components, classes, interfaces
**Audience:** Developers, architects

### Diagram

```mermaid
graph TB
    subgraph "WaitingRoom API (ASP.NET Core)"
        subgraph "Endpoints Layer"
            CheckInEndpoint["Reception Register Endpoint<br/>(POST /api/reception/register)"]
            CashierEndpoint["Cashier Endpoint Cluster<br/>(POST /api/cashier/*)"]
            MedicalEndpoint["Medical Endpoint Cluster<br/>(POST /api/medical/*)"]
            MonitorEndpoint["Monitor Endpoint<br/>(GET /api/v1/waiting-room/{id}/monitor)"]
            QueueStateEndpoint["QueueState Endpoint<br/>(GET /api/v1/waiting-room/{id}/queue-state)"]
            NextTurnEndpoint["NextTurn Endpoint<br/>(GET /api/v1/waiting-room/{id}/next-turn)"]
            HistoryEndpoint["RecentHistory Endpoint<br/>(GET /api/v1/waiting-room/{id}/recent-history)"]
            RebuildEndpoint["Rebuild Endpoint<br/>(POST /api/v1/waiting-room/{id}/rebuild)"]
        end

        subgraph "Application Layer"
            CommandHandlers["Command Handlers<br/>(Reception, Cashier, Medical workflows)"]
            QueryHandler["Query Handlers<br/>(Read from projections)"]

            subgraph "Ports (Interfaces)"
                IEventStore["IEventStore<br/>(Load/Save aggregates)"]
                IEventPublisher["IEventPublisher<br/>(Publish events)"]
                IClock["IClock<br/>(Time abstraction)"]
                IProjectionContext["IWaitingRoomProjectionContext<br/>(Query read models)"]
            end
        end

        subgraph "Domain Layer"
            WaitingQueue["WaitingQueue (Aggregate)<br/>CheckInPatient()<br/>CallNextAtCashier()<br/>ClaimNextPatient()<br/>CompleteAttention()"]
            DomainEvents["Domain Events<br/>PatientCheckedIn<br/>PatientPaymentValidated<br/>PatientClaimedForAttention<br/>PatientAttentionCompleted"]
            ValueObjects["Value Objects<br/>QueueId, PatientId, Priority"]
        end

        subgraph "Infrastructure Layer"
            PostgresEventStore["PostgresEventStore<br/>(implements IEventStore)<br/>SaveAsync(), LoadAsync()"]
            RabbitMqPublisher["RabbitMqEventPublisher<br/>(implements IEventPublisher)<br/>PublishAsync()"]
            SystemClock["SystemClock<br/>(implements IClock)<br/>UtcNow"]
            ProjectionContext["InMemoryWaitingRoomProjectionContext<br/>(implements IProjectionContext)<br/>GetMonitorViewAsync()"]
        end
    end

    subgraph "External Dependencies"
        DB["PostgreSQL<br/>(EventStore + Outbox + ReadModels)"]
        MQ["RabbitMQ<br/>(Topic Exchange)"]
    end

    CheckInEndpoint -->|"calls"| CommandHandlers
    CashierEndpoint -->|"calls"| CommandHandlers
    MedicalEndpoint -->|"calls"| CommandHandlers
    MonitorEndpoint -->|"calls"| QueryHandler
    QueueStateEndpoint -->|"calls"| QueryHandler
    NextTurnEndpoint -->|"calls"| QueryHandler
    HistoryEndpoint -->|"calls"| QueryHandler
    RebuildEndpoint -->|"triggers"| ProjectionContext

    CommandHandlers -->|"uses"| IEventStore
    CommandHandlers -->|"uses"| IEventPublisher
    CommandHandlers -->|"uses"| IClock
    CommandHandlers -->|"executes"| WaitingQueue

    QueryHandler -->|"uses"| IProjectionContext

    WaitingQueue -->|"emits"| DomainEvents
    WaitingQueue -->|"uses"| ValueObjects

    IEventStore -->|"implemented by"| PostgresEventStore
    IEventPublisher -->|"implemented by"| RabbitMqPublisher
    IClock -->|"implemented by"| SystemClock
    IProjectionContext -->|"implemented by"| ProjectionContext

    PostgresEventStore -->|"SQL queries"| DB
    RabbitMqPublisher -->|"AMQP"| MQ

    style CheckInEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style CashierEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style MedicalEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style MonitorEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style QueueStateEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style NextTurnEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style HistoryEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff
    style RebuildEndpoint fill:#1168bd,stroke:#0b4884,color:#ffffff

    style CommandHandlers fill:#438dd5,stroke:#2e6295,color:#ffffff
    style QueryHandler fill:#438dd5,stroke:#2e6295,color:#ffffff

    style WaitingQueue fill:#59c2e6,stroke:#3d8ea3,color:#000000
    style DomainEvents fill:#59c2e6,stroke:#3d8ea3,color:#000000
    style ValueObjects fill:#59c2e6,stroke:#3d8ea3,color:#000000

    style PostgresEventStore fill:#85bbf0,stroke:#5d91c9,color:#000000
    style RabbitMqPublisher fill:#85bbf0,stroke:#5d91c9,color:#000000
    style SystemClock fill:#85bbf0,stroke:#5d91c9,color:#000000
    style ProjectionContext fill:#85bbf0,stroke:#5d91c9,color:#000000
```

### Component Responsibilities

| Component | Responsibility | Dependencies | Testability |
|-----------|---------------|--------------|-------------|
| **Reception Register Endpoint** | Parse HTTP request, validate, call handler | Command handlers | Integration tests (HTTP) |
| **Cashier Endpoint Cluster** | Handle cashier workflow commands (`call-next`, `validate-payment`, alternates) | Command handlers + domain aggregate | Integration tests (HTTP) |
| **Medical Endpoint Cluster** | Handle consulting-room and consultation workflow commands | Command handlers + domain aggregate | Integration tests (HTTP) |
| **Command Handlers** | Orchestrate role workflows: load → execute → save → publish | IEventStore, IEventPublisher, IClock, WaitingQueue | Unit tests (with fakes) |
| **WaitingQueue Aggregate** | Enforce business rules, emit events | None (pure domain) | Unit tests (no mocks) |
| **PostgresEventStore** | Persist events to PostgreSQL Event Store | Npgsql, Dapper | Integration tests (real DB) |
| **RabbitMqEventPublisher** | Publish events to RabbitMQ topic | RabbitMQ.Client | Integration tests (real MQ) |
| **Query Handlers** | Retrieve data from projections | IWaitingRoomProjectionContext | Unit tests (with fakes) |
| **ProjectionContext** | Query read models in runtime projection context | In-memory context implementation | Unit/integration tests |

---

## Supplementary Diagrams

### Event Sourcing Flow

```mermaid
sequenceDiagram
    participant Client
    participant API
    participant Handler as CommandHandler
    participant Aggregate as WaitingQueue
    participant EventStore as PostgresEventStore
    participant DB as PostgreSQL

    Client->>API: POST /api/reception/register
    API->>Handler: HandleAsync(command)

    Handler->>EventStore: LoadAsync(queueId)
    EventStore->>DB: SELECT * FROM event_store WHERE aggregate_id = ?
    DB-->>EventStore: [Event1, Event2, Event3]
    EventStore->>Aggregate: new WaitingQueue()
    EventStore->>Aggregate: ApplyEvent(Event1)
    EventStore->>Aggregate: ApplyEvent(Event2)
    EventStore->>Aggregate: ApplyEvent(Event3)
    EventStore-->>Handler: WaitingQueue (rehydrated)

    Handler->>Aggregate: CheckInPatient(request)
    Aggregate->>Aggregate: Validate business rules
    Aggregate->>Aggregate: Apply(PatientCheckedIn)
    Aggregate-->>Handler: Success

    Handler->>EventStore: SaveAsync(aggregate)

    EventStore->>DB: BEGIN TRANSACTION
    EventStore->>DB: INSERT INTO event_store (...)
    EventStore->>DB: INSERT INTO outbox (...)
    EventStore->>DB: COMMIT TRANSACTION

    DB-->>EventStore: OK
    EventStore-->>Handler: Events saved
    Handler-->>API: eventCount = 1
    API-->>Client: 200 OK

    Note over DB,EventStore: Events + Outbox saved atomically
```

---

### CQRS Flow

```mermaid
graph LR
    subgraph "WRITE MODEL"
        Command[Command:<br/>CheckInPatient] --> Handler[Command Handler]
        Handler --> Aggregate[WaitingQueue<br/>Aggregate]
        Aggregate --> EventStore[(Event Store)]
        EventStore --> Outbox[(Outbox Table)]
    end

    subgraph "EVENT BUS"
        Worker[Outbox Worker] --> RabbitMQ[RabbitMQ<br/>Topic Exchange]
    end

    subgraph "READ MODEL"
        RabbitMQ --> Projection[Projection Handler]
        Projection --> ReadDB[(Read Database:<br/>Denormalized Views)]
        Query[Query:<br/>GetQueueStatus] --> QueryHandler[Query Handler]
        QueryHandler --> ReadDB
    end

    Outbox -.->|Poll| Worker

    style Command fill:#ff6b6b,stroke:#c92a2a,color:#ffffff
    style Query fill:#51cf66,stroke:#2b8a3e,color:#ffffff
    style EventStore fill:#1c7ed6,stroke:#1864ab,color:#ffffff
    style ReadDB fill:#20c997,stroke:#087f5b,color:#ffffff
```

---

### Outbox Pattern

```mermaid
sequenceDiagram
    participant Handler as CommandHandler
    participant DB as PostgreSQL
    participant Worker as Outbox Worker
    participant MQ as RabbitMQ
    participant Projection as Projection Handler

    Handler->>DB: BEGIN TRANSACTION
    Handler->>DB: INSERT INTO event_store (event_data)
    Handler->>DB: INSERT INTO outbox (event_data, published=false)
    Handler->>DB: COMMIT TRANSACTION

    Note over DB: Events + Outbox committed atomically

    loop Every 5 seconds
        Worker->>DB: SELECT * FROM outbox WHERE published=false LIMIT 100
        DB-->>Worker: [event1, event2, ...]

        loop For each event
            Worker->>MQ: Publish event
            MQ-->>Worker: ACK
            Worker->>DB: UPDATE outbox SET published=true WHERE id=?
        end
    end

    MQ->>Projection: event delivery
    Projection->>DB: UPDATE read_models SET ...

    Note over Projection,DB: Eventual consistency:<br/>Lag typically <100ms
```

---

### Deployment View

```mermaid
graph TB
    subgraph "Docker Compose (Local)"
        subgraph "Container: rlapp-api"
            API[WaitingRoom.API<br/>Port: 5000]
        end

        subgraph "Container: rlapp-worker"
            Worker[WaitingRoom.Worker]
        end

        subgraph "Container: postgres"
            PostgreSQL[PostgreSQL 16<br/>Port: 5432]
        end

        subgraph "Container: rabbitmq"
            RabbitMQ[RabbitMQ 3.12<br/>Port: 5672<br/>Management: 15672]
        end

        subgraph "Container: prometheus"
            Prometheus[Prometheus<br/>Port: 9090]
        end

        subgraph "Container: grafana"
            Grafana[Grafana<br/>Port: 3000]
        end
    end

    API -->|TCP| PostgreSQL
    API -->|HTTP| Prometheus
    Worker -->|TCP| PostgreSQL
    Worker -->|AMQP| RabbitMQ
    Worker -->|HTTP| Prometheus

    Prometheus -->|Scrape| API
    Prometheus -->|Scrape| Worker

    Grafana -->|Query| Prometheus

    style API fill:#1168bd,stroke:#0b4884,color:#ffffff
    style Worker fill:#1168bd,stroke:#0b4884,color:#ffffff
    style PostgreSQL fill:#2d9c5e,stroke:#1f6b41,color:#ffffff
    style RabbitMQ fill:#ff6600,stroke:#cc5200,color:#ffffff
    style Prometheus fill:#e6522c,stroke:#b33f1e,color:#ffffff
    style Grafana fill:#f46800,stroke:#c45300,color:#ffffff
```

**Network:**

- All containers on `rlapp_network` bridge
- PostgreSQL persistent volume: `postgres_data`
- RabbitMQ persistent volume: `rabbitmq_data`

---

## 📚 Diagram Legend

### Colors

| Color | Meaning |
|-------|---------|
| **Blue (#1168bd)** | PRIMARY CONTAINERS (API, Worker) |
| **Light Blue (#438dd5)** | APPLICATION LAYER (Handlers, Ports) |
| **Sky Blue (#59c2e6)** | DOMAIN LAYER (Aggregates, Events) |
| **Lighter Blue (#85bbf0)** | INFRASTRUCTURE LAYER (Adapters) |
| **Green (#2d9c5e)** | DATABASE (PostgreSQL) |
| **Orange (#ff6600)** | MESSAGE BROKER (RabbitMQ) |
| **Red (#e6522c)** | MONITORING (Prometheus, Grafana) |

### C4 Model Levels

1. **Level 1 (System Context):** The big picture — what does the system do and who uses it?
2. **Level 2 (Container):** Zoomed into the system — applications, databases, message brokers
3. **Level 3 (Component):** Zoomed into a container — classes, interfaces, components
4. **Level 4 (Code):** Zoomed into a component — UML class diagrams (not shown here)

---

## 📖 References

- **C4 Model:** <https://c4model.com>
- **Simon Brown:** "Software Architecture for Developers"
- **Mermaid Docs:** <https://mermaid.js.org>
- [ARCHITECTURE.md](../ARCHITECTURE.md) — Complete architecture documentation
- [ADR-007: Hexagonal Architecture](ADR-007-hexagonal-architecture.md)

---

**Last Updated:** 2026-02-19
**Maintained By:** Architecture Team
