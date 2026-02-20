# ARCHITECTURE_GUARDRAILS.md

**RLAPP — Architectural Integrity Protection System**

Este documento define las **reglas inquebrantables** que protegen la arquitectura del sistema contra degradación, acoplamiento indebido y corrupción del dominio.

La arquitectura **NO es opcional**.
Si una regla se rompe, **el cambio debe rechazarse**.

---

# 1. Principio Supremo

> El Dominio es soberano, puro e independiente.
> Nada externo puede contaminarlo.

---

# 2. Reglas Fundamentales (No Negociables)

## 2.1 Independencia del Dominio

El `Domain`:

* NO depende de infraestructura
* NO conoce frameworks
* NO usa ORM
* NO usa HTTP
* NO usa logging externo
* NO usa brokers
* NO usa librerías técnicas
* NO usa fechas del sistema directamente (usar Value Objects / abstractions)

Permitido:

* Lógica de negocio pura
* Entidades
* Value Objects
* Domain Events
* Invariantes
* Agregados
* Reglas del dominio

Si el Dominio importa algo externo → **VIOLACIÓN ARQUITECTÓNICA**

---

## 2.2 Application es Orquestación, NO Negocio

La capa `Application`:

Debe:

* Orquestar casos de uso
* Coordinar agregados
* Manejar transacciones lógicas
* Publicar eventos

NO debe:

* Contener reglas de negocio
* Acceder directamente a DB
* Usar implementaciones concretas
* Conocer frameworks
* Tener lógica de infraestructura

---

## 2.3 Infraestructura es Reemplazable

Todo componente externo debe poder reemplazarse sin modificar:

* Domain
* Application

Incluye:

* Base de datos
* Broker de mensajería
* Event Store
* Framework web
* Realtime gateway
* Cache
* Scheduler

Si cambiar infraestructura rompe Domain/Application → **Arquitectura incorrecta**

---

## 2.4 Regla de Dependencias

Dirección obligatoria:

```
Domain ← Application ← Adapters ← Infrastructure
```

NUNCA al revés.

---

## 2.5 Event Sourcing es Fuente de Verdad

Reglas:

* Los eventos son inmutables
* Nunca se editan
* Nunca se eliminan
* El estado se reconstruye desde eventos
* Los eventos representan hechos del dominio (no técnicos)
* Versionado obligatorio si evolucionan
* Idempotencia obligatoria

---

## 2.6 CQRS Obligatorio

Separación estricta:

* Write Model → Dominio + Eventos
* Read Model → Proyecciones optimizadas

Los Read Models:

* Pueden romper normalización
* Pueden regenerarse
* NO contienen lógica de negocio

---

## 2.7 Tests Arquitectónicamente Puros

Los tests del dominio deben correr sin:

* DB
* Docker
* HTTP
* Broker
* Infraestructura

Si un test necesita infraestructura → **Violación**

---

## 2.8 Controllers Son Adaptadores, NO Cerebro

Los Controllers:

* NO contienen lógica de negocio
* NO deciden reglas
* NO acceden a DB
* SOLO traducen transporte → Application

---

## 2.9 Reglas de Eventos

Un Domain Event:

Debe:

* Representar algo que **ya ocurrió**
* Ser inmutable
* Tener nombre en pasado
* Ser semántico
* No contener lógica

No debe:

* Contener servicios
* Contener repositorios
* Ser técnico
* Ser mutable

---

## 2.10 Idempotencia Obligatoria

Todo comando, handler y proyección debe soportar:

* Reintentos
* Duplicados
* Reprocesamiento

Sin efectos secundarios inconsistentes.

---

# 3. Señales de Degradación Arquitectónica

Si aparece cualquiera de estos síntomas:

* Dominio accede a DB
* Controllers con lógica
* Application usando infraestructura concreta
* Tests dependientes de DB
* Eventos técnicos (ej: `RowInserted`)
* Servicios anémicos sin invariantes
* Lógica en repositorios
* Acoplamiento a framework
* Dependencias circulares

→ **La arquitectura se está degradando**

Debe corregirse inmediatamente.

---

# 4. Regla de Reemplazo

Pregunta obligatoria ante cualquier cambio:

> Si reemplazo DB, broker o framework…
> ¿Debe cambiar el Dominio?

Si la respuesta es **sí** → el cambio está mal diseñado.

---

# 5. Regla de Complejidad

Está prohibido:

* Overengineering sin valor
* Patrones innecesarios
* Abstracciones sin motivo
* Capas ficticias
* Indirección artificial

La arquitectura protege el dominio, **no complica el sistema**.

---

# 6. Regla de Invariantes

Las invariantes del dominio:

* Viven dentro del agregado
* Nunca fuera
* Nunca en Application
* Nunca en Controllers
* Nunca en DB

Si una regla puede romperse → el modelo está mal.

---

# 7. Regla de Eventos vs Estado

Incorrecto:

```
Guardar estado → luego evento
```

Correcto:

```
Evento → fuente de verdad → estado derivado
```

---

# 8. Regla de Observabilidad

Debe existir:

* Logging estructurado
* Correlation ID
* Trazabilidad de eventos
* Auditoría por Event Store
* Métricas operativas

Pero **sin contaminar el Dominio**.

---

# 9. Regla de Evolución

El sistema debe permitir:

* Agregar eventos sin romper históricos
* Agregar proyecciones sin tocar dominio
* Cambiar infraestructura sin rediseñar negocio
* Versionar eventos
* Reprocesar historia completa

---

# 10. Definition of Architecture Done

La arquitectura está sana si:

* Dominio puro
* Dependencias correctas
* Event sourcing consistente
* CQRS separado
* Tests puros
* Infraestructura reemplazable
* Sin acoplamientos técnicos
* Invariantes protegidas
* Eventos correctos
* Proyecciones regenerables

---

# 11. La Regla Final

Si algo funciona pero rompe la arquitectura → **Está mal**

Si algo tarda más pero protege el dominio → **Está bien**

La arquitectura es un activo permanente.
El código es temporal.

