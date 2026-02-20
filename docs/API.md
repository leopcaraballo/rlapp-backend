# RLAPP API Contract (Current Runtime)

Contrato HTTP vigente de `WaitingRoom.API` alineado al estado real del código.

Modelo operativo definitivo (target de negocio): [OPERATING_MODEL.md](OPERATING_MODEL.md)

## Alcance y fuente de verdad

Este documento describe los endpoints **publicados actualmente** en el pipeline HTTP principal.

Fuente de verdad operativa:

1. Mapeo de endpoints en `src/Services/WaitingRoom/WaitingRoom.API/Program.cs`
2. OpenAPI runtime (`/openapi/v1.json`) en entorno Development

---

## Base URL

- Local con `dotnet run`: `http://localhost:5000`
- Containerizado (si API está expuesta por compose): normalmente `http://localhost:8080`

---

## Headers comunes

- `Content-Type: application/json`
- `X-Correlation-Id: <uuid>` (recomendado)

Si el cliente no envía `X-Correlation-Id`, el middleware lo genera y lo devuelve en el response header.

---

## Errores (formato estándar)

El middleware global normaliza excepciones a este contrato:

```json
{
  "error": "DomainViolation",
  "message": "Queue is at maximum capacity (50). Cannot add more patients.",
  "correlationId": "7f6c0bb7-2d68-497f-9b77-8768208d2895"
}
```

Mapeo principal:

- `400`: violación de reglas de dominio o request inválido
- `404`: agregado/cola no encontrado
- `409`: conflicto de concurrencia
- `500`: error inesperado

---

## Command Endpoints

## Recepción

### POST /api/reception/register

Registro clínico operativo (alias de check-in por rol de recepción).

Mismo contrato de request/response que `POST /api/waiting-room/check-in`.

## Taquilla

### POST /api/cashier/call-next

Llama siguiente paciente para pago, aplicando prioridad administrativa primero y FIFO dentro de nivel.

### POST /api/cashier/validate-payment

Valida pago y habilita paso a cola de consulta.

### POST /api/cashier/mark-payment-pending

Marca pago pendiente e incrementa contador de intentos (máximo 3).

### POST /api/cashier/mark-absent

Marca ausencia en taquilla y reencola paciente (máximo 2 reintentos).

### POST /api/cashier/cancel-payment

Cancela turno por política de pago (después de alcanzar intentos máximos).

## Médico

### POST /api/medical/consulting-room/activate

Activa consultorio para habilitar llamados médicos desde ese consultorio.

### POST /api/medical/consulting-room/deactivate

Desactiva consultorio; desde ese momento no puede reclamar siguiente paciente.

### POST /api/medical/call-next

Reclama siguiente paciente para consulta.

Regla clave: `stationId` debe corresponder a un consultorio activo, de lo contrario retorna `400` por violación de dominio.

### POST /api/medical/start-consultation

Inicia consulta para paciente en estado `LlamadoConsulta`.

### POST /api/medical/finish-consultation

Finaliza consulta para paciente en estado `EnConsulta`.

### POST /api/medical/mark-absent

Marca ausencia en consulta; primer ausente reintenta, segundo ausente cancela por ausencia.

## Compatibilidad (legacy)

### POST /api/waiting-room/check-in

Registra el check-in de un paciente en una cola de espera.

### Request body

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "patientName": "Juan Pérez",
  "priority": "High",
  "consultationType": "General",
  "actor": "nurse-001",
  "notes": "Dolor de cabeza"
}
```

### Validaciones relevantes

- `queueId`: obligatorio, no vacío
- `patientId`: obligatorio, no vacío
- `patientName`: obligatorio
- `priority`: obligatorio; valores válidos `Low | Medium | High | Urgent`
- `consultationType`: obligatorio; longitud entre 2 y 100
- `actor`: obligatorio
- `notes`: opcional

### Success response (200)

```json
{
  "success": true,
  "message": "Patient checked in successfully",
  "correlationId": "f47ac10b-58cc-4372-a567-0e02b2c3d479",
  "eventCount": 1
}
```

### Error responses

- `400` DomainViolation
- `404` AggregateNotFound
- `409` ConcurrencyConflict
- `500` InternalServerError

### Ejemplo curl

```bash
curl -X POST http://localhost:5000/api/waiting-room/check-in \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: $(uuidgen)" \
  -d '{
    "queueId": "QUEUE-01",
    "patientId": "PAT-001",
    "patientName": "Juan Perez",
    "priority": "High",
    "consultationType": "General",
    "actor": "nurse-001",
    "notes": "Dolor de cabeza"
  }'
```

### POST /api/waiting-room/claim-next

Reclama el siguiente paciente a atender (prioridad clínica + orden de llegada).

#### Request body

```json
{
  "queueId": "QUEUE-01",
  "actor": "doctor-001",
  "stationId": "CONSULT-03"
}
```

`stationId` es obligatorio en operación para cumplir la regla de consultorio activo.

#### Response (200)

```json
{
  "success": true,
  "message": "Patient claimed successfully",
  "correlationId": "3c3ad6dc-6725-4600-8968-6285a7a7b3a6",
  "eventCount": 1,
  "patientId": "PAT-001"
}
```

### POST /api/waiting-room/call-patient

Marca el paciente reclamado como llamado para atención.

#### Request body

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "nurse-001"
}
```

#### Response (200)

```json
{
  "success": true,
  "message": "Patient called successfully",
  "correlationId": "abfced71-84db-4dbd-8004-f84db9f4cf31",
  "eventCount": 1,
  "patientId": "PAT-001"
}
```

### POST /api/waiting-room/complete-attention

Finaliza la atención del paciente activo.

#### Request body

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "doctor-001",
  "outcome": "resolved",
  "notes": "Alta y control en 48h"
}
```

#### Response (200)

```json
{
  "success": true,
  "message": "Attention completed successfully",
  "correlationId": "237abf39-bfc2-4b93-8f7c-b6629b1512f6",
  "eventCount": 1,
  "patientId": "PAT-001"
}
```

---

## Health & Readiness

### GET /health/live

Verifica que el proceso está vivo.

### GET /health/ready

Verifica readiness completa (incluye chequeos de dependencias).

### Ejemplo curl (health)

```bash
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready
```

---

## OpenAPI

### GET /openapi/v1.json

Disponible en entorno Development.

Usar este endpoint para generación de cliente frontend o validación automática del contrato publicado.

---

## Query Endpoints

Todos estos endpoints están publicados en runtime:

- `GET /api/v1/waiting-room/{queueId}/monitor`
- `GET /api/v1/waiting-room/{queueId}/queue-state`
- `GET /api/v1/waiting-room/{queueId}/next-turn`
- `GET /api/v1/waiting-room/{queueId}/recent-history?limit=20`
- `POST /api/v1/waiting-room/{queueId}/rebuild`

### Ejemplo response `GET /api/v1/waiting-room/{queueId}/next-turn`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "patientName": "Juan Pérez",
  "priority": "high",
  "consultationType": "General",
  "status": "called",
  "claimedAt": "2026-02-19T14:10:00Z",
  "calledAt": "2026-02-19T14:11:00Z",
  "stationId": "CONSULT-03",
  "projectedAt": "2026-02-19T14:11:01Z"
}
```

---

## Integración Frontend

Para consumo end-to-end, tipado TypeScript, estrategia de retries, manejo de errores y trazabilidad:

- [frontend-api-usage.md](api/frontend-api-usage.md)

---

## Related docs

- [APPLICATION.md](APPLICATION.md)
- [ARCHITECTURE.md](ARCHITECTURE.md)
- [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md)
- [ADR-005-cqrs.md](architecture/ADR-005-cqrs.md)
- [ADR-006-outbox-pattern.md](architecture/ADR-006-outbox-pattern.md)

---

**Last updated:** 2026-02-19
**Status:** Runtime-aligned
