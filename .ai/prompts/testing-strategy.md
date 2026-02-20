# TESTING_STRATEGY.md

**RLAPP — Estrategia Oficial de Testing**

## 1. Principio Fundamental

Si el dominio es correcto → el sistema es correcto.
El testing protege:

* Invariantes
* Consistencia por eventos
* Idempotencia
* Determinismo
* Reprocesamiento

---

## 2. Pirámide de Testing RLAPP

### Nivel 1 — Unit Tests (Dominio puro) ⭐ CRÍTICO

Sin:

* DB
* HTTP
* Broker
* Infraestructura
* Docker

Validan:

* Invariantes del agregado
* Emisión correcta de eventos
* Reglas del negocio
* Determinismo
* Estados inválidos
* Concurrencia lógica

---

### Nivel 2 — Application Tests

Validan:

* Orquestación correcta
* Uso de puertos
* Manejo de comandos
* Transacciones lógicas

Con mocks/fakes — nunca infraestructura real.

---

### Nivel 3 — Projection Tests

Validan:

* Proyecciones correctas desde eventos
* Idempotencia
* Reprocesamiento
* Reconstrucción completa

---

### Nivel 4 — Integration Tests

Con infraestructura real:

* Event Store
* Broker
* DB lectura
* Outbox
* Reintentos
* DLQ

---

### Nivel 5 — E2E Tests

Validan flujo completo:

Check-in → Cola → Llamado → Monitor → Finalización

---

## 3. Reglas Obligatorias

* El dominio debe pasar tests en aislamiento total
* Tests deben ser determinísticos
* Debe validarse reprocesamiento completo
* Debe probarse duplicación de eventos
* Debe probarse idempotencia
* Debe probarse concurrencia

---

## 4. Tests Especiales de Event Sourcing

* Replay completo desde cero
* Replay con eventos duplicados
* Replay con fallos intermedios
* Evolución de versión de evento
* Snapshot + replay
* Orden incorrecto

---

## 5. Definition of Test Done

El sistema es seguro si:

* Dominio protegido por tests
* Event sourcing reproducible
* Idempotencia garantizada
* Proyecciones reconstruibles
* Sin dependencia infra en unit tests

