# SCALABILITY.md

**RLAPP — Estrategia de Escalabilidad**

## 1. Objetivo

Escalar sin romper consistencia ni dominio.

---

## 2. Escalado del Write Model

* Particionado por AggregateId
* Concurrencia optimista
* Event Store distribuido

---

## 3. Escalado del Read Model

* Proyecciones paralelas
* Read replicas
* Cache opcional
* Regeneración independiente

---

## 4. Escalado del Realtime Monitor

* Gateway horizontal
* Pub/Sub
* Fan-out por sala
* Backpressure control

---

## 5. Escalado de Eventos

* Particiones por flujo
* Ordering por Aggregate
* Retry + DLQ
* Idempotencia obligatoria

---

## 6. Límites de Diseño

El sistema debe soportar:

* Alto throughput de eventos
* Reprocesamiento completo
* Fallos parciales
* Escalado horizontal
* Infra reemplazable

