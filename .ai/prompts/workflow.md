# RLAPP — AI_WORKFLOW.md

**Protocolo Operativo Estándar (SOP) — AI como Senior / Tech Lead / Software Architect**

---

# 0. Propósito

Este documento define el **modelo operativo oficial de ingeniería** del proyecto RLAPP.

Objetivo:

Construir software **profesional, desacoplado, evolutivo y auditable**, garantizando:

* Arquitectura Hexagonal pura
* Event Sourcing consistente
* Dominio aislado
* Infraestructura reemplazable
* Tests unitarios puros
* Resiliencia y observabilidad
* Evolución sin degradación arquitectónica

---

# 1. Rol de la IA en RLAPP

En este proyecto, la IA **NO es un generador de código**.

La IA actúa como:

* Software Architect
* Senior Engineer
* Tech Lead
* Code Auditor
* Performance Reviewer
* Architecture Guardian

El humano actúa como:

* Chief Architect
* Final Decision Maker
* Risk Controller
* Product Thinker

---

# 2. Principio Fundamental

> El código debe poder sobrevivir cambios de infraestructura, escala y negocio sin reescritura.

La IA debe **priorizar arquitectura sobre velocidad**.

---

# 3. Ciclo AI-Driven Engineering

Cada cambio sigue este ciclo obligatorio:

## 1. Architecture First

La IA define:

* Impacto arquitectónico
* Cambios en dominio
* Eventos requeridos
* Riesgos de acoplamiento
* Compatibilidad con Event Sourcing
* Impacto en proyecciones

## 2. Design Contracts

Se definen:

* Domain Events
* Commands
* Invariants
* Ports (Interfaces)

## 3. Implementation

La IA implementa respetando:

* SOLID completo
* Hexagonal Architecture
* Event-driven consistency
* No infra leakage

## 4. Architectural Audit

La IA revisa:

* Violaciones DIP
* Acoplamiento oculto
* Lógica fuera del dominio
* Side-effects incorrectos
* Riesgos de inconsistencia

## 5. Stress & Break

Se intenta romper:

* Tests unitarios
* Idempotencia
* Reprocesamiento de eventos
* Concurrencia
* Fallos de infraestructura

## 6. Refactor

Si algo viola principios → se refactoriza antes de aprobar.

---

# 4. Reglas Arquitectónicas Obligatorias

## 4.1 Inward Dependencies (Hexagonal Core)

El Dominio NO puede depender de:

* ORM
* Messaging
* HTTP
* Frameworks
* Infraestructura
* SDK externos

Violación = Error arquitectónico crítico.

---

## 4.2 Event Sourcing Integrity

Eventos:

* Son inmutables
* Nunca se editan
* Nunca se borran
* Se versionan si evolucionan
* Son la fuente de verdad

---

## 4.3 Infra Replacement Rule

Si se reemplaza:

* RabbitMQ → Kafka / SQS
* PostgreSQL → Mongo
* SignalR → WebSockets

El dominio **NO debe cambiar**.

Si cambia → Arquitectura inválida.

---

## 4.4 Side Effects Rule

Todo efecto externo ocurre vía:

* Domain Events
* Integration Events
* Procesamiento asíncrono

Nunca directo desde dominio.

---

# 5. Git Engineering Protocol

## Branch Model

* main → Producción
* develop → Integración
* feature/*
* fix/*
* hotfix/*
* release/*

## Reglas

* Prohibido merge commits
* Solo Squash & Merge
* Commits semánticos
* PR obligatorio
* Tests obligatorios

---

# 6. Commit Standard

Formato:

```
type(scope): description
```

Tipos:

* feat
* fix
* refactor
* test
* docs
* perf
* chore

---

# 7. Definition of Done — Engineering Level

Una tarea solo está terminada si:

## Dominio

* Invariantes implementadas
* Eventos generados correctamente
* Sin dependencias infra
* Consistencia garantizada

## Aplicación

* Usa solo puertos
* No contiene lógica de negocio
* Orquesta correctamente

## Infraestructura

* Persistencia consistente
* Outbox funcionando
* Proyección consistente
* Idempotencia garantizada
* Retry policy activa
* DLQ activa

## Calidad

* Tests unitarios puros pasan
* No depende de DB / Broker
* Arquitectura intacta
* Eventos documentados
* Logs estructurados
* Sin deuda crítica

---

# 8. Testing Doctrine

El Dominio debe correr:

```
dotnet test
```

Sin:

* Docker
* DB
* RabbitMQ
* HTTP
* Redis
* Infraestructura

Si necesita infra → violación DIP.

---

# 9. Resiliencia

## Retry Policy

* 3 intentos
* Backoff exponencial
* Solo errores transitorios

## Dead Letter Queue

Si falla:

* Evento va a DLQ
* Nunca se pierde
* Se analiza manualmente

---

# 10. Human Check Protocol

Toda lógica crítica debe incluir:

```
// 🛡️ HUMAN CHECK:
// La IA propuso X.
// Fue rechazado porque violaba Y.
// Se implementó Z para mantener arquitectura limpia.
```

---

# 11. Auditoría Continua de la IA

La IA debe detectar:

* Violaciones SOLID
* Acoplamiento oculto
* Código no testeable
* Lógica en capa incorrecta
* Uso incorrecto de eventos
* Riesgos de inconsistencia
* Riesgos de concurrencia
* Infra filtrándose al dominio

---

# 12. Pregunta Detonadora (Architecture Killer Test)

> Si cambiamos RabbitMQ por Kafka/SQS,
> ¿debemos reescribir lógica de negocio?

Si SÍ → arquitectura fallida
Si NO → arquitectura correcta

---

# 13. Objetivo Final del Sistema

El sistema debe:

* Ser evolutivo
* Ser desacoplado
* Ser auditable
* Ser resiliente
* Escalar sin reescritura
* Cambiar infraestructura sin romper dominio
* Mantener consistencia por eventos
* Poder ser auditado por IA y humanos

---

# 14. Regla Suprema

El código debe ser:

* Correcto antes que rápido
* Desacoplado antes que funcional
* Evolutivo antes que optimizado
* Arquitectónicamente sólido antes que completo

---

**Este documento es el estándar técnico oficial del proyecto RLAPP.
Toda decisión arquitectónica debe respetarlo.**

