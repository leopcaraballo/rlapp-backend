# PROJECTION_STRATEGY.md

**RLAPP — Estrategia de Proyecciones (Read Models)**

## 1. Principio

Las proyecciones son derivadas, no fuente de verdad.

---

## 2. Reglas

* Sin lógica de negocio
* Idempotentes
* Reprocesables
* Eventualmente consistentes
* Desnormalización permitida

---

## 3. Tipos de Proyección

* WaitingRoomMonitorView
* QueueStateView
* PatientFlowMetrics
* HistoricalAuditView

---

## 4. Rebuild Strategy

Debe poder:

* Borrar proyección
* Reprocesar eventos
* Reconstruir estado completo

---

## 5. Versionado de Proyección

Si cambia el schema:

* Crear nueva versión
* Rebuild completo

---

## 6. Consistency Lag

Se debe medir:

Event Store → Projection → Monitor

---

## 7. Idempotencia

Eventos duplicados no deben corromper proyección.

