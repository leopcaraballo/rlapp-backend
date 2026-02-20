# RLAPP API Contract (Current Runtime)

Contrato HTTP vigente de `WaitingRoom.API` alineado al estado real del código.

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

## Command Endpoint

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

---

## Health & Readiness

### GET /health/live

Verifica que el proceso está vivo.

### GET /health/ready

Verifica readiness completa (incluye chequeos de dependencias).

### Ejemplo curl

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

## Estado de endpoints de query/proyecciones

Existen implementaciones de query endpoints en:

- `src/Services/WaitingRoom/WaitingRoom.API/Endpoints/WaitingRoomQueryEndpoints.cs`

Ejemplos:

- `GET /api/v1/waiting-room/{queueId}/monitor`
- `GET /api/v1/waiting-room/{queueId}/queue-state`
- `POST /api/v1/waiting-room/{queueId}/rebuild`

Actualmente **no están registrados** en `Program.cs`, por lo que no forman parte del contrato HTTP publicado hoy.

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
