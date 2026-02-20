# Frontend API Usage Guide (RLAPP WaitingRoom)

Guía práctica para que el frontend consuma la API de forma correcta, predecible y alineada a la arquitectura del backend.

## 1) Estado real de la API (hoy)

Basado en la configuración actual de `WaitingRoom.API/Program.cs`, los endpoints **activos** son:

- `POST /api/waiting-room/check-in`
- `GET /health/live`
- `GET /health/ready`
- `GET /openapi/v1.json` (solo en entorno Development)

### ¿Por qué importa esto?

En `docs/API.md` aparecen endpoints de query (`/api/v1/waiting-room/{queueId}/...`), pero actualmente no están registrados en `Program.cs`. Para frontend, la fuente de verdad operativa es:

1. Endpoints mapeados en código.
2. OpenAPI generado en runtime (`/openapi/v1.json`) cuando está disponible.

---

## 2) Flujo recomendado desde Frontend

### 2.1 Check-in de paciente (flujo principal)

1. Validar datos en UI.
2. Enviar `POST /api/waiting-room/check-in`.
3. Guardar y propagar `correlationId` de la respuesta para trazabilidad.
4. Mostrar confirmación o error normalizado.

### ¿Por qué así?

- El backend usa Event Sourcing + validaciones de dominio.
- Un error funcional no debe verse como fallo técnico genérico.
- `correlationId` permite soporte rápido (frontend ↔ backend ↔ logs).

---

## 3) Contrato de `POST /api/waiting-room/check-in`

## Request headers

- `Content-Type: application/json`
- `X-Correlation-Id: <uuid>` (opcional, pero recomendado)

Si no se envía `X-Correlation-Id`, el backend lo genera y lo retorna en response header.

### ¿Por qué enviar `X-Correlation-Id` desde frontend?

Para correlacionar la misma acción de usuario entre:

- logs de browser,
- logs de API,
- monitoreo/observabilidad.

## Request body

```json
{
  "queueId": "queue-001",
  "patientId": "pat-123",
  "patientName": "Juan Perez",
  "priority": "High",
  "consultationType": "General",
  "notes": "Dolor torácico",
  "actor": "nurse-01"
}
```

## Reglas de validación relevantes para frontend

- `queueId`: obligatorio, no vacío.
- `patientId`: obligatorio, no vacío.
- `patientName`: obligatorio.
- `priority`: obligatorio, valores válidos: `Low`, `Medium`, `High`, `Urgent` (case-insensitive al enviar).
- `consultationType`: obligatorio, longitud entre 2 y 100.
- `actor`: obligatorio.
- `notes`: opcional.

### ¿Por qué validar también en frontend si backend ya valida?

- Mejora UX (feedback inmediato).
- Reduce round-trips innecesarios.
- Mantiene consistencia de formularios antes de invocar API.

## Success response (200)

```json
{
  "success": true,
  "message": "Patient checked in successfully",
  "correlationId": "2d6be1cc-2f96-4635-8d6a-3a1b2fe4c949",
  "eventCount": 1
}
```

Interpretación:

- `success`: operación aceptada y procesada.
- `eventCount`: cantidad de eventos generados por el comando.
- `correlationId`: id para trazabilidad.

## Error response (formato)

Para errores manejados por middleware global:

```json
{
  "error": "DomainViolation",
  "message": "Queue is at maximum capacity (50). Cannot add more patients.",
  "correlationId": "..."
}
```

Mapeo práctico esperado:

- `400`: violación de dominio / request inválido.
- `404`: agregado/cola no existe.
- `409`: conflicto de concurrencia.
- `500`: error inesperado.

### ¿Por qué diferenciar por tipo de error?

Porque la acción UX cambia:

- `400`: mostrar mensaje funcional al usuario.
- `404`: sugerir refrescar catálogo/selección de cola.
- `409`: reintento controlado o refresco de estado.
- `500`: fallback + mensaje técnico genérico + tracking.

---

## 4) Health endpoints para frontend

- `GET /health/live`: verifica que el proceso está vivo.
- `GET /health/ready`: verifica readiness completa (incluye dependencias).

### ¿Cuándo usarlos desde frontend?

- Pantallas de diagnóstico interno/soporte.
- Verificación previa en herramientas administrativas.

No se recomienda bloquear UX de usuario final por health checks periódicos desde browser.

---

## 5) Implementación sugerida (Frontend)

## 5.1 Cliente HTTP centralizado

Centralizar en un solo módulo:

- `baseUrl` por entorno.
- Inyección automática de `X-Correlation-Id`.
- Normalización de errores.
- Timeout y retry para errores transitorios.

## 5.2 Política de retry

- Reintentar solo en: `409`, `5xx`, timeouts/transient network.
- No reintentar en: `400`, `404`.
- Usar backoff exponencial con jitter.

### ¿Por qué?

Evita duplicar tráfico y previene ocultar errores funcionales del usuario.

## 5.3 Tipado TypeScript mínimo

```ts
export type Priority = 'Low' | 'Medium' | 'High' | 'Urgent';

export interface CheckInPatientRequest {
  queueId: string;
  patientId: string;
  patientName: string;
  priority: Priority;
  consultationType: string;
  notes?: string;
  actor: string;
}

export interface CheckInPatientResponse {
  success: boolean;
  message: string;
  correlationId: string;
  eventCount: number;
}

export interface ApiError {
  error: string;
  message: string;
  correlationId?: string;
}
```

---

## 6) Diferencia entre “documentado” y “publicado”

Actualmente:

- Hay endpoints de query documentados y con implementación parcial en `Endpoints/WaitingRoomQueryEndpoints.cs`.
- Pero no están publicados en pipeline HTTP porque no se mapean en `Program.cs`.

### Recomendación operativa para frontend

Tomar como contrato vigente solo lo que esté:

1. En OpenAPI runtime (`/openapi/v1.json`) o
2. Confirmado en endpoints mapeados en `Program.cs`.

---

## 7) Checklist de integración frontend

- [ ] Usar cliente HTTP único.
- [ ] Enviar/generar `X-Correlation-Id` por request.
- [ ] Validar formulario antes de invocar API.
- [ ] Manejar explícitamente `400`, `404`, `409`, `500`.
- [ ] Registrar `correlationId` en logs de frontend.
- [ ] Basar contrato en endpoints activos (no solo en documentación estática).

---

## Referencias

- `src/Services/WaitingRoom/WaitingRoom.API/Program.cs`
- `src/Services/WaitingRoom/WaitingRoom.API/Middleware/CorrelationIdMiddleware.cs`
- `src/Services/WaitingRoom/WaitingRoom.API/Middleware/ExceptionHandlerMiddleware.cs`
- `src/Services/WaitingRoom/WaitingRoom.Application/DTOs/CheckInPatientDto.cs`
- `src/Services/WaitingRoom/WaitingRoom.Domain/ValueObjects/Priority.cs`
- `src/Services/WaitingRoom/WaitingRoom.Domain/ValueObjects/ConsultationType.cs`
- `docs/API.md`
