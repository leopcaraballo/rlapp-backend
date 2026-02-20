# ADR.md

**RLAPP — Architecture Decision Records**

Este documento registra las **Decisiones Arquitectónicas (ADR)** del proyecto.
Cada decisión es **inmutable**, trazable y justificable.
Las ADR permiten entender *por qué* el sistema es como es, no solo *cómo* está construido.

---

# Formato Oficial ADR

```
ADR-XXX — Título

Status: Proposed | Accepted | Rejected | Superseded
Date: YYYY-MM-DD
Decision Makers:

## Context
Problema, restricciones, drivers arquitectónicos, riesgos.

## Decision
Decisión tomada.

## Consequences
Impactos positivos y negativos.

## Alternatives Considered
Opciones evaluadas y por qué se descartaron.

## Tradeoffs
Costos vs beneficios.

## Notes
Información adicional.
```

---

# ADR-001 — Arquitectura Hexagonal

**Status:** Accepted
**Date:** 2026-02-19
**Decision Makers:** Arquitectura RLAPP

## Context

Se requiere:

* Independencia de frameworks
* Alta testabilidad
* Bajo acoplamiento
* Evolución sin romper dominio

## Decision

Adoptar **Arquitectura Hexagonal (Ports & Adapters)**:

* Domain en el centro
* Application orquesta
* Adapters implementan infraestructura
* Infraestructura reemplazable

## Consequences

### Positivas

* Dominio puro
* Tests sin infraestructura
* Bajo acoplamiento
* Alta mantenibilidad

### Negativas

* Mayor complejidad inicial
* Más abstracciones

## Alternatives Considered

* Arquitectura en Capas → alto acoplamiento
* MVC tradicional → dominio débil
* Clean sin Hexagonal → menos explícita en puertos

## Tradeoffs

Más diseño inicial vs mayor longevidad del sistema.

---

# ADR-002 — Domain Driven Design (DDD)

**Status:** Accepted
**Date:** 2026-02-19

## Context

El sistema posee reglas complejas del negocio y necesita:

* Modelo expresivo
* Invariantes fuertes
* Evolución controlada

## Decision

Adoptar **DDD táctico y estratégico**:

* Aggregates
* Value Objects
* Domain Events
* Bounded Contexts
* Ubiquitous Language

## Consequences

### Positivas

* Dominio fuerte
* Modelo consistente
* Menor deuda técnica

### Negativas

* Curva de aprendizaje
* Mayor disciplina de diseño

## Alternatives

* Modelo anémico → descartado
* CRUD tradicional → descartado

---

# ADR-003 — Event Sourcing

**Status:** Accepted
**Date:** 2026-02-19

## Context

Se requiere:

* Auditoría completa
* Historial inmutable
* Reprocesamiento
* Debug temporal

## Decision

Adoptar **Event Sourcing**:

* Eventos como fuente de verdad
* Estado derivado
* Eventos inmutables
* Versionado obligatorio

## Consequences

### Positivas

* Trazabilidad total
* Rebuild del sistema
* Auditoría nativa

### Negativas

* Mayor complejidad
* Requiere disciplina

## Alternatives

* CRUD persistente → sin historial
* Snapshot-only → sin trazabilidad

---

# ADR-004 — CQRS

**Status:** Accepted
**Date:** 2026-02-19

## Context

Separar:

* Escritura con reglas fuertes
* Lectura optimizada

## Decision

Implementar **CQRS estricto**:

* Write Model → Dominio + Eventos
* Read Model → Proyecciones
* Separación lógica estricta

## Consequences

### Positivas

* Lecturas rápidas
* Write model limpio
* Escalabilidad

### Negativas

* Eventual consistency
* Complejidad operativa

---

# ADR-005 — Dependency Inversion

**Status:** Accepted
**Date:** 2026-02-19

## Decision

El dominio **NO depende de infraestructura**.

Infraestructura depende del dominio mediante:

* Ports
* Interfaces
* Adapters

## Consequences

Infra reemplazable sin romper negocio.

---

# ADR-006 — Outbox Pattern

**Status:** Accepted

## Context

Evitar inconsistencias entre:

* Persistencia
* Publicación de eventos

## Decision

Implementar **Outbox Pattern**:

* Eventos guardados transaccionalmente
* Publicación asincrónica confiable

## Consequences

* Consistencia garantizada
* Sin pérdida de eventos

---

# ADR-007 — Tests sin Infraestructura

**Status:** Accepted

## Decision

Tests unitarios:

* Sin DB
* Sin HTTP
* Sin brokers
* Solo dominio puro

Infra se prueba en integración.

---

# ADR-008 — Eventos Inmutables

**Status:** Accepted

## Decision

Eventos:

* Nunca se editan
* Nunca se borran
* Siempre versionados

---

# ADR-009 — Arquitectura Evolutiva

**Status:** Accepted

El sistema debe poder:

* Cambiar infraestructura
* Escalar horizontalmente
* Evolucionar sin romper dominio

---

# ADR-010 — Regla Fundamental

Si una decisión:

* Introduce acoplamiento
* Debilita el dominio
* Rompe Event Sourcing
* Viola SOLID
* Reduce testabilidad

→ Debe registrarse como nueva ADR y justificarse.

---

# Gobernanza de ADR

Toda decisión arquitectónica:

1. Debe registrarse
2. Debe justificarse
3. No puede ser implícita
4. No puede romper ADR previas sin "Superseded"

---

# Estados ADR

* **Proposed** → En evaluación
* **Accepted** → Activa
* **Rejected** → Descartada
* **Superseded** → Reemplazada por otra

---

# Regla de Ingeniería

Sin ADR → No hay cambio arquitectónico válido.

---

**Este documento es obligatorio para evolución del sistema.**
