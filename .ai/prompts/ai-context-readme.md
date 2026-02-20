# RLAPP — Real-Time Waiting Room Flow Engine

Sistema distribuido basado en **Arquitectura Hexagonal + Event Sourcing + AI-Driven Engineering** para la **orquestación en tiempo real del flujo de pacientes en una sala de espera**.

RLAPP no es un simple sistema de citas. Es un **motor de flujo clínico en vivo**, desacoplado, auditable, resiliente y preparado para evolución sin degradación arquitectónica.

---

# 1. Visión del Sistema

RLAPP gobierna el ciclo completo del flujo de pacientes en tiempo real:

* Check-in de pacientes
* Gestión de cola dinámica
* Llamado a consultorio
* Monitor en vivo
* Métricas operativas
* Consistencia basada en eventos

El sistema está diseñado para:

* Escalar horizontalmente
* Cambiar infraestructura sin romper el dominio
* Ejecutar lógica de negocio en aislamiento total
* Mantener consistencia mediante Event Sourcing

---

# 2. Principios de Ingeniería

Este proyecto cumple estrictamente:

* Arquitectura Hexagonal (Ports & Adapters)
* Event Sourcing como fuente de verdad
* CQRS (Write vs Read separation)
* SOLID completo
* Dominio aislado
* Infraestructura reemplazable
* Tests unitarios puros
* Observabilidad y resiliencia
* AI como Software Architect y Auditor

---

# 3. Arquitectura de Alto Nivel

```
Core
 ├── Domain              → Lógica de negocio pura
 ├── Application         → Orquestación (Use Cases)
 │    ├── Commands
 │    ├── Queries
 │    └── Ports (Interfaces)

Adapters
 ├── Driving
 │    ├── API
 │    ├── Realtime Gateway
 │
 ├── Driven
 │    ├── Event Store
 │    ├── Persistence
 │    ├── Messaging (Broker)
 │    └── Projections
```

## Regla Arquitectónica

El **Dominio nunca depende de infraestructura**.

---

# 4. Event Sourcing

El sistema usa eventos como fuente de verdad.

Características:

* Eventos inmutables
* Nunca se editan ni eliminan
* Versionado si evolucionan
* Reconstrucción completa del estado
* Consistencia eventual en Read Models

Eventos clave del flujo:

* PatientCheckedIn
* PatientQueued
* PatientCalled
* ConsultationStarted
* PatientCompleted
* PatientNoShow
* QueueReordered
* WaitingRoomClosed

---

# 5. Monitor en Tiempo Real

El sistema genera una proyección optimizada:

## WaitingRoomMonitorView

* Posición en cola
* Ticket anonimizado
* Estado
* Tiempo estimado
* Consultorio
* Paciente llamado
* Timestamp

Actualización:

```
Domain Event → Projection → Realtime Gateway → Monitor
```

---

# 6. Testing Strategy

El dominio debe ejecutarse en aislamiento total.

## Tests Unitarios Puros

Deben correr sin:

* Docker
* Base de datos
* Broker de mensajería
* HTTP
* Infraestructura externa

Si el dominio necesita infraestructura → violación arquitectónica.

---

# 7. Resiliencia

## Retry Policy

* 3 intentos
* Backoff exponencial
* Solo errores transitorios

## Dead Letter Queue

Si falla tras retries:

* El evento va a DLQ
* Nunca se pierde
* Se analiza manualmente

---

# 8. AI-Driven Engineering

La IA en este proyecto actúa como:

* Software Architect
* Senior Engineer
* Tech Lead
* Code Auditor
* Architecture Guardian

El humano:

* Valida decisiones
* Controla riesgos
* Protege arquitectura

Ver: `AI_WORKFLOW.md`

---

# 9. Estructura del Repositorio

```
src/
 ├── Services/
 │    ├── WaitingRoom/
 │    │    ├── Domain/
 │    │    ├── Application/
 │    │    ├── Infrastructure/
 │    │    ├── API/
 │    │    └── Worker/
 │
docs/
 ├── ARCHITECTURE.md
 ├── DOMAIN_MODEL.md
 ├── EVENTS.md
 ├── PERSISTENCE.md
 ├── OPERATIONAL_RUNBOOK.md
 ├── API_DOCUMENTATION.md
 ├── AI_WORKFLOW.md
 └── DEBT_REPORT.md
```

---

# 10. Cómo Ejecutar el Proyecto

## Requisitos

* Docker
* Docker Compose
* .NET SDK
* Git

## Levantar entorno local

```
docker compose up -d
dotnet build
```

## Ejecutar tests

```
dotnet test
```

---

# 11. Git Flow

Ramas oficiales:

* main → Producción
* develop → Integración
* feature/*
* fix/*
* hotfix/*
* release/*

Reglas:

* Squash & Merge obligatorio
* Commits semánticos
* PR obligatorio
* Tests obligatorios

---

# 12. Estándar de Commits

Formato:

```
type(scope): message
```

Ejemplo:

```
feat(waiting-room): add CallNextPatient use case
```

Tipos:

* feat
* fix
* refactor
* test
* docs
* chore
* perf

---

# 13. Definition of Done

Una tarea solo está terminada si:

* El dominio mantiene invariantes
* Eventos generados correctamente
* No hay dependencias de infraestructura en Domain/Application
* Tests unitarios puros pasan
* Event Store consistente
* Proyección consistente
* Idempotencia garantizada
* Documentación actualizada
* Arquitectura intacta

---

# 14. Lo que la IA hizo mal

(Esta sección se mantiene viva)

Ejemplos típicos:

* Intentó usar DbContext dentro del Dominio
* Intentó acoplar RabbitMQ en Application
* Generó tests dependientes de DB
* Propuso lógica de negocio en Controllers
* Rompió DIP usando infraestructura concreta

Cada error fue corregido para mantener:

* Hexagonal pura
* Event Sourcing consistente
* Dominio desacoplado

---

# 15. Pregunta Detonadora

Si se reemplaza:

* Broker de mensajería
* Base de datos
* Gateway en tiempo real

¿Debe cambiar el Dominio?

Si la respuesta es **sí**, la arquitectura es incorrecta.

---

# 16. Objetivo Final

RLAPP debe ser:

* Evolutivo
* Desacoplado
* Testeable
* Resiliente
* Auditable
* Escalable
* Consistente por eventos
* Arquitectónicamente estable en el tiempo

---

# 17. Licencia

Uso interno / académico / arquitectónico.

---

# 18. Nota Final

Este proyecto prioriza:

Arquitectura > Velocidad
Consistencia > Conveniencia
Diseño > Implementación

La arquitectura no es un detalle. Es el sistema.

