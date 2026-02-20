# ANTI_PATTERNS.md

**RLAPP — Catálogo Oficial de Anti-Patrones Prohibidos**

Este documento enumera prácticas **explícitamente prohibidas** dentro del proyecto RLAPP.
Su objetivo es prevenir degradación arquitectónica, corrupción del dominio, acoplamiento indebido y pérdida de testabilidad.

Si alguno de estos anti-patrones aparece, **debe corregirse inmediatamente**.

---

# 1. Dominio Contaminado (Infrastructure Leakage)

## Descripción

El Dominio depende de infraestructura, frameworks o detalles técnicos.

## Ejemplos Prohibidos

* Uso de ORM dentro del Dominio
* Importar librerías de mensajería
* Acceso a HTTP o APIs externas
* Uso de `DateTime.Now` directo
* Logging técnico dentro del Dominio

## Consecuencia

* Violación de Arquitectura Hexagonal
* Dominio no testeable
* Acoplamiento rígido

## Corrección

* Introducir puertos (interfaces)
* Usar Value Objects / abstractions
* Mover dependencias a Adapters

---

# 2. Application con Lógica de Negocio

## Descripción

La capa Application contiene reglas del negocio.

## Ejemplos

* Validaciones de invariantes fuera del agregado
* Decisiones de negocio en handlers
* Reglas de transición de estado fuera del dominio

## Consecuencia

* Modelo anémico
* Dominio débil
* Reglas duplicadas

## Corrección

Mover lógica al Aggregate Root.

---

# 3. Controllers Inteligentes

## Descripción

El Controller toma decisiones de negocio.

## Ejemplos

* Calcular reglas del dominio
* Acceder directamente a DB
* Ejecutar lógica condicional del negocio
* Construir entidades manualmente

## Consecuencia

* Violación de capas
* Código no testeable
* Acoplamiento al framework

## Corrección

Controller = traductor → Application.

---

# 4. CRUD Disfrazado de DDD

## Descripción

El sistema se reduce a operaciones CRUD sin modelo de dominio.

## Síntomas

* Entidades sin comportamiento
* Sin invariantes
* Sin eventos
* Lógica en servicios o repositorios

## Consecuencia

* No es DDD
* No es Event Sourcing
* Dominio anémico

## Corrección

Introducir:

* Aggregates
* Invariants
* Domain Events

---

# 5. Event Sourcing Incorrecto

## Problemas

* Editar eventos
* Borrar eventos
* Guardar estado como fuente primaria
* Eventos técnicos (`RowInserted`)
* Eventos mutables
* No versionar eventos

## Consecuencia

* Corrupción histórica
* Inconsistencia
* Imposibilidad de reprocesamiento

## Corrección

Eventos = hechos del dominio, inmutables, versionados.

---

# 6. Acoplamiento a Infraestructura

## Descripción

El negocio depende de:

* RabbitMQ
* Base de datos específica
* Framework web
* Cache concreto

## Síntoma

Cambiar infraestructura rompe el dominio.

## Corrección

Dependency Inversion + Ports & Adapters.

---

# 7. Repositorios con Lógica

## Descripción

El repositorio contiene lógica de negocio.

## Ejemplo

* Calcular estados
* Aplicar reglas
* Decidir comportamiento

## Consecuencia

* Dominio fragmentado
* Lógica dispersa

## Corrección

Repositorio = persistencia, NO negocio.

---

# 8. Servicios Anémicos

## Descripción

Servicios con toda la lógica y entidades pasivas.

## Consecuencia

* No es DDD
* No hay invariantes
* Lógica duplicada

## Corrección

Mover comportamiento al Aggregate.

---

# 9. Tests Dependientes de Infraestructura

## Problema

Tests requieren:

* DB
* Docker
* HTTP
* Broker

## Consecuencia

* No son unitarios
* Violación DIP
* Tests frágiles

## Corrección

Mocks / Fakes / Ports.

---

# 10. Violación de CQRS

## Problemas

* Read Model con lógica de negocio
* Escritura directa a proyecciones
* Mezclar lectura y escritura

## Consecuencia

* Inconsistencia
* Modelo corrupto

## Corrección

Separación estricta Write vs Read.

---

# 11. Falta de Idempotencia

## Problema

El sistema falla ante:

* Eventos duplicados
* Reintentos
* Reprocesamiento

## Consecuencia

* Corrupción de estado
* Inconsistencia

## Corrección

Diseño idempotente en:

* Handlers
* Projections
* Consumers

---

# 12. Big Ball of Mud

## Síntomas

* Dependencias circulares
* Sin capas
* Sin dominio claro
* Lógica dispersa
* Infra mezclada con negocio

## Consecuencia

Sistema no evolutivo.

## Corrección

Refactor a Hexagonal + DDD.

---

# 13. Overengineering

## Problema

* Patrones innecesarios
* Abstracciones artificiales
* Complejidad sin valor

## Consecuencia

Sistema difícil de mantener.

## Corrección

Simplicidad orientada al dominio.

---

# 14. Event Handler con Side Effects Directos

## Problema

Un handler modifica múltiples sistemas sin control.

## Consecuencia

* Inconsistencia
* Fallos parciales
* Difícil recuperación

## Corrección

Outbox + procesamiento controlado.

---

# 15. Lógica en Proyecciones

## Problema

El Read Model decide reglas del negocio.

## Consecuencia

Modelo inconsistente.

## Corrección

Proyecciones = derivación, NO decisión.

---

# 16. Falta de Invariantes

## Problema

El sistema permite estados inválidos.

## Consecuencia

Dominio incorrecto.

## Corrección

Invariantes dentro del Aggregate Root.

---

# 17. Arquitectura Cosmética

## Problema

Capas existen pero no protegen nada.

## Ejemplos

* Dominio depende de infra
* Application contiene lógica
* Adapters vacíos

## Consecuencia

Arquitectura falsa.

## Corrección

Refactor real a Hexagonal.

---

# 18. Ignorar Observabilidad

## Problema

No hay:

* Logs estructurados
* Correlation IDs
* Trazabilidad

## Consecuencia

Sistema no auditable.

---

# 19. Falta de Versionado de Eventos

## Problema

Cambiar schema rompe históricos.

## Corrección

Versionado + Upcasting.

---

# 20. Regla Final

Si algo funciona pero introduce:

* Acoplamiento
* Violación SOLID
* Dependencia infra
* Dominio débil
* Event sourcing incorrecto
* Tests frágiles

→ Es un **Anti-Patrón** y debe corregirse.

---

# 21. Responsabilidad de Ingeniería

Todo ingeniero debe:

* Detectar anti-patrones
* Corregirlos
* Documentarlos
* Evitar su reaparición

La arquitectura no se protege sola.

---

**Este documento es obligatorio para revisiones técnicas y PR.**
Si un cambio introduce un anti-patrón, el PR debe rechazarse.

