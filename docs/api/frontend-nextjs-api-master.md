# Documento Maestro — Funcionamiento de API en Frontend (Next.js)

Guía integral para implementar el frontend de RLAPP en Next.js, alineado al comportamiento real de la API, al modelo operativo por roles y al patrón CQRS + Event Sourcing.

---

## 1. Objetivo y alcance

Este documento define cómo debe interactuar el frontend con el backend para:

- Ejecutar comandos operativos (write model).
- Leer estado materializado en proyecciones (read model).
- Mostrar el monitor público de citas de forma consistente.
- Manejar errores funcionales y técnicos sin romper UX.
- Soportar trazabilidad completa con `X-Correlation-Id`.

Aplica para todos los módulos de UI: recepción, taquilla, médico y pantalla pública.

---

## 2. Principio operativo obligatorio (CQRS en frontend)

En esta app, el frontend **no consume eventos del event store ni del broker directamente**.

Debe operar así:

1. Ejecuta una acción con endpoint `POST` (comando).
2. Toma la respuesta como confirmación de aceptación (`success`, `eventCount`, `correlationId`).
3. Refresca vistas de lectura (`GET /api/v1/waiting-room/...`) para ver el estado actualizado.

### Regla clave

- **Command response ≠ estado final de pantalla**.
- **Projection query = fuente de verdad visual**.

---

## 3. Contratos transversales

### 3.1 Headers

Enviar siempre:

- `Content-Type: application/json`
- `X-Correlation-Id: <uuid>`

Si no se envía, backend lo genera y lo retorna en response header.

### 3.2 Respuesta de éxito típica (comandos)

Patrón común:

```json
{
  "success": true,
  "message": "...",
  "correlationId": "...",
  "eventCount": 1,
  "patientId": "..."
}
```

`patientId` no siempre viene; depende del comando.

### 3.3 Respuesta de error estándar (middleware global)

```json
{
  "error": "DomainViolation",
  "message": "...",
  "correlationId": "..."
}
```

Mapeo HTTP esperado:

- `400` → `DomainViolation` (regla funcional/estado inválido)
- `404` → `AggregateNotFound`
- `409` → `ConcurrencyConflict`
- `500` → `InternalServerError`

### 3.4 Política UX por tipo de error

- `400`: mostrar mensaje funcional al usuario (sin retry automático).
- `404`: sugerir recargar contexto (cola/paciente inexistente).
- `409`: refrescar proyección + retry controlado.
- `500`: mensaje genérico + tracking con `correlationId`.

---

## 4. Endpoints activos (runtime)

### 4.1 Commands (write model)

#### Recepción

- `POST /api/reception/register`

#### Taquilla

- `POST /api/cashier/call-next`
- `POST /api/cashier/validate-payment`
- `POST /api/cashier/mark-payment-pending`
- `POST /api/cashier/mark-absent`
- `POST /api/cashier/cancel-payment`

#### Médico

- `POST /api/medical/consulting-room/activate`
- `POST /api/medical/consulting-room/deactivate`
- `POST /api/medical/call-next`
- `POST /api/medical/start-consultation`
- `POST /api/medical/finish-consultation`
- `POST /api/medical/mark-absent`

#### Compatibilidad legacy

- `POST /api/waiting-room/check-in`
- `POST /api/waiting-room/claim-next`
- `POST /api/waiting-room/call-patient`
- `POST /api/waiting-room/complete-attention`

### 4.2 Queries (read model/proyecciones)

- `GET /api/v1/waiting-room/{queueId}/monitor`
- `GET /api/v1/waiting-room/{queueId}/queue-state`
- `GET /api/v1/waiting-room/{queueId}/next-turn`
- `GET /api/v1/waiting-room/{queueId}/recent-history?limit=20`
- `POST /api/v1/waiting-room/{queueId}/rebuild` (operativo/admin)

### 4.3 Salud del sistema

- `GET /health/live`
- `GET /health/ready`

---

## 5. Payloads de commands (contratos de request)

## 5.1 Recepción

### `POST /api/reception/register`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "patientName": "Juan Perez",
  "priority": "High",
  "consultationType": "General",
  "age": 68,
  "isPregnant": false,
  "notes": "Dolor torácico",
  "actor": "reception-01"
}
```

## 5.2 Taquilla

### `POST /api/cashier/call-next`

```json
{
  "queueId": "QUEUE-01",
  "actor": "cashier-01",
  "cashierDeskId": "DESK-01"
}
```

### `POST /api/cashier/validate-payment`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "cashier-01",
  "paymentReference": "PAY-123"
}
```

### `POST /api/cashier/mark-payment-pending`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "cashier-01",
  "reason": "Tarjeta rechazada"
}
```

### `POST /api/cashier/mark-absent`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "cashier-01"
}
```

### `POST /api/cashier/cancel-payment`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "cashier-01",
  "reason": "Superó intentos máximos"
}
```

## 5.3 Médico

### `POST /api/medical/consulting-room/activate`

```json
{
  "queueId": "QUEUE-01",
  "consultingRoomId": "CONSULT-03",
  "actor": "doctor-01"
}
```

### `POST /api/medical/consulting-room/deactivate`

```json
{
  "queueId": "QUEUE-01",
  "consultingRoomId": "CONSULT-03",
  "actor": "doctor-01"
}
```

### `POST /api/medical/call-next`

```json
{
  "queueId": "QUEUE-01",
  "actor": "doctor-01",
  "stationId": "CONSULT-03"
}
```

### `POST /api/medical/start-consultation`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "doctor-01"
}
```

### `POST /api/medical/finish-consultation`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "doctor-01",
  "outcome": "resolved",
  "notes": "Alta con control"
}
```

### `POST /api/medical/mark-absent`

```json
{
  "queueId": "QUEUE-01",
  "patientId": "PAT-001",
  "actor": "doctor-01"
}
```

---

## 6. Máquina de estados funcional (referencia frontend)

Ruta principal:

`Registrado -> EnEsperaTaquilla -> EnTaquilla -> PagoValidado -> EnEsperaConsulta -> LlamadoConsulta -> EnConsulta -> Finalizado`

Estados alternos:

- `PagoPendiente`
- `AusenteTaquilla`
- `CanceladoPorPago`
- `AusenteConsulta`
- `CanceladoPorAusencia`

Reglas operativas que UI debe respetar:

1. No pasa a consulta sin pago validado.
2. No inicia consulta sin paciente llamado/reclamado.
3. No doble turno activo por paciente.
4. Alta prioridad antes que normal.
5. `medical/call-next` requiere consultorio activo (si no, `400`).

Límites operativos:

- Pago pendiente: máximo 3 intentos.
- Ausencia en taquilla: máximo 2 reintentos.
- Ausencia en consulta: 1 reintento antes de cancelación.

---

## 7. Lecturas de proyección y mapeo a UI

## 7.1 `GET /monitor`

Campos principales:

- `queueId`
- `totalPatientsWaiting`
- `highPriorityCount`
- `normalPriorityCount`
- `lowPriorityCount`
- `lastPatientCheckedInAt`
- `averageWaitTimeMinutes`
- `utilizationPercentage`
- `projectedAt`

Uso UI:

- KPIs operativos y resumen de carga.
- Indicador de frescura de datos con `projectedAt`.

## 7.2 `GET /queue-state`

Campos principales:

- `queueId`, `currentCount`, `maxCapacity`, `isAtCapacity`, `availableSpots`, `projectedAt`
- `patientsInQueue[]` con `patientId`, `patientName`, `priority`, `checkInTime`, `waitTimeMinutes`

Uso UI:

- Tabla/lista principal de pacientes en espera.
- Alertas de capacidad.

## 7.3 `GET /next-turn`

Campos principales:

- `patientId`, `patientName`, `priority`, `consultationType`, `status`
- `claimedAt`, `calledAt`, `stationId`, `projectedAt`

`status` observado en proyecciones:

- `cashier-called`
- `claimed`
- `called`
- `waiting` (fallback de endpoint query cuando no hay turno activo)

Uso UI:

- Banner de turno activo/próximo.
- Referencia de estación/consultorio cuando aplique.

## 7.4 `GET /recent-history`

Registro de atenciones completadas:

- `queueId`, `patientId`, `patientName`, `priority`, `consultationType`, `completedAt`, `outcome`, `notes`

Uso UI:

- Bitácora reciente para operación y trazabilidad.

---

## 8. Orquestación frontend por rol (command → refresh)

## 8.1 Recepción

### Acción: registrar paciente

- Command: `POST /api/reception/register`
- Luego refrescar:
  - `GET /queue-state`
  - `GET /monitor`
  - `GET /next-turn` (opcional si hay vista compartida)

## 8.2 Taquilla

### Acción: llamar siguiente

- Command: `POST /api/cashier/call-next`
- Refrescar:
  - `GET /next-turn`
  - `GET /queue-state`
  - `GET /monitor`

### Acción: validar pago

- Command: `POST /api/cashier/validate-payment`
- Refrescar:
  - `GET /queue-state`
  - `GET /monitor`
  - `GET /next-turn`

### Acciones alternas: pendiente/ausente/cancelar

- Commands:
  - `mark-payment-pending`
  - `mark-absent`
  - `cancel-payment`
- Refrescar:
  - `GET /queue-state`
  - `GET /monitor`
  - `GET /next-turn`
  - `GET /recent-history` (si panel de historial visible)

## 8.3 Médico

### Activar/desactivar consultorio

- Commands:
  - `POST /api/medical/consulting-room/activate`
  - `POST /api/medical/consulting-room/deactivate`
- Refrescar:
  - `GET /next-turn`
  - `GET /queue-state`

### Llamar siguiente

- Command: `POST /api/medical/call-next` (con `stationId`)
- Refrescar:
  - `GET /next-turn`
  - `GET /queue-state`
  - `GET /monitor`

### Iniciar/finalizar consulta y ausencia

- Commands:
  - `start-consultation`
  - `finish-consultation`
  - `mark-absent`
- Refrescar:
  - `GET /next-turn`
  - `GET /queue-state`
  - `GET /monitor`
  - `GET /recent-history`

---

## 9. Monitor público de citas (pantalla pública)

Objetivo: mostrar información operativa sin acciones de escritura.

## 9.1 Fuentes de datos

- Principal: `GET /api/v1/waiting-room/{queueId}/next-turn`
- KPIs: `GET /api/v1/waiting-room/{queueId}/monitor`
- Complemento opcional: `GET /recent-history?limit=5`

## 9.2 Información a mostrar

### Bloque A — Turno actual/próximo (prioridad máxima)

- Nombre/identificador visible del paciente (según política de privacidad).
- Estado del turno (`cashier-called`, `claimed`, `called`, `waiting`).
- Estación/consultorio (`stationId`) cuando exista.
- Hora de reclamo/llamado (`claimedAt`, `calledAt`).

### Bloque B — Estado general de cola

- Total esperando.
- Conteo por prioridad (alta/normal/baja).
- Tiempo promedio de espera.
- Porcentaje de utilización.

### Bloque C — Frescura y disponibilidad

- Sello “actualizado hace Xs” con `projectedAt`.
- Estado vacío cuando `next-turn` no tenga turno (`404`): “Sin turnos en este momento”.

### Bloque D — Historial breve (opcional)

- Últimos 5 atendidos (nombre/alias + hora).

## 9.3 Frecuencia de actualización recomendada

- Polling normal: cada 2–3 segundos (`next-turn` y `monitor`).
- En transición reciente: revalidación rápida 0.5s → 1s → 2s (máx 3 intentos).
- Si falla temporalmente, conservar último estado renderizado + aviso de reconexión.

## 9.4 Privacidad para pantalla pública

El backend expone `patientName` y `patientId`; el frontend debe decidir política de exhibición:

- Opción recomendada: enmascarar (`JU*** PE***`, `***-001`).
- Evitar mostrar notas clínicas en pantalla pública.

---

## 10. Estrategia técnica en Next.js

## 10.1 Patrón recomendado: BFF con Route Handlers

Centralizar llamadas al backend en rutas internas de Next.js para:

- Inyectar `X-Correlation-Id` automáticamente.
- Estandarizar manejo de errores/retries/timeouts.
- Evitar exponer detalles internos al browser.

## 10.2 Capa de cliente API única

La capa debe resolver:

- `baseUrl` por ambiente.
- `fetch` con timeout.
- normalización de éxito/error.
- retry con backoff + jitter en `409`, `5xx`, timeout/red.
- no retry en `400`, `404`.

## 10.3 Estado y cache

- Acciones de comando: mutation explícita.
- Consultas: cache por `queueId` y endpoint.
- Tras comando exitoso: invalidar/refetch de proyecciones afectadas.

## 10.4 Eventual consistency (UX)

Durante la ventana entre command y proyección:

- Mostrar estado “actualizando…”
- Mantener UI previa estable.
- Confirmar transición solo cuando query refleje el cambio.

---

## 11. Matriz de consistencia (comando vs lectura esperada)

| Comando | Lecturas a refrescar | Señal esperada en read model |
|---|---|---|
| `reception/register` | `queue-state`, `monitor` | sube conteo y aparece paciente |
| `cashier/call-next` | `next-turn`, `queue-state`, `monitor` | turno `cashier-called`, baja cola consulta/espera según flujo |
| `cashier/validate-payment` | `queue-state`, `monitor`, `next-turn` | paciente entra a cola de consulta / next-turn se limpia o rota |
| `cashier/mark-payment-pending` | `queue-state`, `monitor`, `next-turn` | se mantiene paciente con estado operativo pendiente |
| `cashier/mark-absent` | `queue-state`, `next-turn`, `monitor` | reencolado o transición por política |
| `cashier/cancel-payment` | `queue-state`, `monitor`, `recent-history` | salida de flujo por cancelación |
| `medical/consulting-room/activate` | `next-turn`, `queue-state` | habilita llamada médica por `stationId` |
| `medical/call-next` | `next-turn`, `queue-state`, `monitor` | turno `claimed` |
| `medical/start-consultation` | `next-turn`, `queue-state` | turno `called`/en consulta |
| `medical/finish-consultation` | `next-turn`, `queue-state`, `monitor`, `recent-history` | cierra turno y agrega histórico |
| `medical/mark-absent` | `next-turn`, `queue-state`, `recent-history` | reintento/cancelación por ausencia |

---

## 12. Health y operación frontend

Uso recomendado:

- `health/live`: diagnóstico de proceso vivo.
- `health/ready`: diagnóstico de dependencias.

No bloquear flujo principal de usuario por health checks periódicos.

---

## 13. Contratos TypeScript base sugeridos

```ts
export interface ApiError {
  error: string;
  message: string;
  correlationId?: string;
}

export interface CommandSuccess {
  success: boolean;
  message: string;
  correlationId: string;
  eventCount: number;
  patientId?: string;
}

export interface WaitingRoomMonitorView {
  queueId: string;
  totalPatientsWaiting: number;
  highPriorityCount: number;
  normalPriorityCount: number;
  lowPriorityCount: number;
  lastPatientCheckedInAt: string | null;
  averageWaitTimeMinutes: number;
  utilizationPercentage: number;
  projectedAt: string;
}

export interface NextTurnView {
  queueId: string;
  patientId: string;
  patientName: string;
  priority: string;
  consultationType: string;
  status: "cashier-called" | "claimed" | "called" | "waiting" | string;
  claimedAt: string | null;
  calledAt: string | null;
  stationId: string | null;
  projectedAt: string;
}
```

---

## 14. Checklist de implementación Next.js

- [ ] Cliente HTTP/BFF centralizado.
- [ ] Generar/propagar `X-Correlation-Id` en cada request.
- [ ] Normalizar errores y mapear UX por código HTTP.
- [ ] Aplicar invalidación/refetch post-command.
- [ ] Implementar polling para monitor público.
- [ ] Mostrar `projectedAt` (frescura de datos).
- [ ] Registrar `correlationId` en logs de frontend.
- [ ] Evitar exponer datos sensibles en monitor público.

---

## 15. Referencias de código y documentación

- `src/Services/WaitingRoom/WaitingRoom.API/Program.cs`
- `src/Services/WaitingRoom/WaitingRoom.API/Endpoints/WaitingRoomQueryEndpoints.cs`
- `src/Services/WaitingRoom/WaitingRoom.API/Middleware/CorrelationIdMiddleware.cs`
- `src/Services/WaitingRoom/WaitingRoom.API/Middleware/ExceptionHandlerMiddleware.cs`
- `src/Services/WaitingRoom/WaitingRoom.Application/DTOs/*.cs`
- `src/Services/WaitingRoom/WaitingRoom.Projections/Views/*.cs`
- `src/Services/WaitingRoom/WaitingRoom.Projections/Handlers/*.cs`
- `docs/OPERATING_MODEL.md`
- `docs/API.md`
- `docs/APPLICATION.md`
- `docs/api/frontend-api-usage.md`

---

## 16. Nota final de diseño

Actualmente el backend documenta una brecha para paralelismo médico completo por múltiples consultorios en una misma cola. Mientras esa evolución no esté implementada, frontend debe asumir una estrategia conservadora de concurrencia para turno médico activo por cola y validar siempre estado proyectado antes de habilitar acciones críticas.
