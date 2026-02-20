# OBSERVABILITY.md

**RLAPP — Observabilidad, Trazabilidad y Auditoría**

## 1. Objetivo

Sistema distribuido sin observabilidad = sistema ciego.

Debe poder responder:

* Qué pasó
* Cuándo
* Por qué
* Dónde falló
* Cómo reproducirlo

---

## 2. Logging Obligatorio

Logging estructurado:

* CorrelationId
* Command
* Event
* AggregateId
* Latencia
* Resultado

Nunca contaminar el Dominio.

---

## 3. Correlation Tracking

Cada comando genera CorrelationId único.

Flujo trazable:

Command → Domain Event → Integration Event → Projection → Monitor

---

## 4. Métricas Críticas

* Tiempo promedio de espera
* Pacientes en cola
* Throughput de eventos
* Latencia de proyección
* Lag del monitor en tiempo real
* Retries
* DLQ count

---

## 5. Alertas

Debe alertar si:

* Proyección se retrasa
* DLQ crece
* Event Store falla
* Monitor no actualiza
* Lag excede umbral

---

## 6. Auditoría

El Event Store permite:

* Reconstruir historia completa
* Trazar flujo de cada paciente
* Detectar inconsistencias

