# EVENT_MODEL_STANDARD.md

**RLAPP — Estándar Oficial de Modelado de Eventos**

## 1. Principio

Los eventos son la fuente de verdad.

---

## 2. Reglas Obligatorias

Un evento debe:

* Ser inmutable
* Representar algo que YA ocurrió
* Tener nombre en pasado
* Ser semántico
* Versionarse si cambia

---

## 3. Naming

Correcto:

* PatientCheckedIn
* PatientCalled
* ConsultationStarted

Incorrecto:

* InsertRow
* UpdateStatus

---

## 4. Metadata Estándar

Cada evento debe contener:

* EventId
* AggregateId
* Version
* Timestamp
* CorrelationId
* CausationId
* Actor
* IdempotencyKey

---

## 5. Versionado

Si el schema cambia:

* No romper eventos históricos
* Usar versionado
* Usar upcasting

---

## 6. Idempotencia

Eventos deben poder procesarse múltiples veces sin corromper estado.

---

## 7. Orden

El orden importa dentro del Aggregate.
Debe garantizarse secuencia consistente.

---

## 8. Snapshot Strategy

Snapshots solo para performance.
La verdad sigue siendo el Event Store.

---

## 9. Domain vs Integration Events

Domain → hechos del negocio
Integration → comunicación entre bounded contexts

Nunca mezclar.

