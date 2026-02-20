# WaitingRoom Operating Model (Definitivo)

## Objetivo

Definir el modelo operativo de clínica para producción con flujo estricto, trazabilidad completa y separación por rol.

## Principios

1. Máquina de estados estricta.
2. FIFO como regla base.
3. Prioridad administrativa automática para gestantes, menores y adultos > 65.
4. Pago obligatorio en taquilla.
5. Múltiples médicos según consultorios activos.
6. Pantallas independientes por rol.
7. Sin pago online.
8. Sin citas por horario.
9. Todo cambio de estado debe generar evento auditable.

## Prioridad automática

La prioridad se asigna en registro:

- Gestante: Alta
- Menor de edad: Alta
- Mayor de 65: Alta
- Resto: Normal

Reglas de cola:

- Se atiende primero la cola prioritaria.
- Dentro de cada nivel se respeta FIFO.

## Máquina de estados canónica

Ruta principal:

`Registrado -> EnEsperaTaquilla -> EnTaquilla -> PagoValidado -> EnEsperaConsulta -> LlamadoConsulta -> EnConsulta -> Finalizado`

Estados alternativos:

- `PagoPendiente`
- `AusenteTaquilla`
- `CanceladoPorPago`
- `AusenteConsulta`
- `CanceladoPorAusencia`

## Reglas de transición obligatorias

1. No pasa a consulta sin `PagoValidado`.
2. No inicia consulta sin `LlamadoConsulta`.
3. No se permite doble turno activo por paciente.
4. Prioridad alta siempre antes que normal.
5. Consultorios activos determinan disponibilidad médica.
6. Transiciones inválidas se rechazan con error de dominio.

## Modelo de colas

Taquilla:

- `ColaPrioritariaTaquilla`
- `ColaNormalTaquilla`

Consulta:

- `ColaPrioritariaConsulta`
- `ColaNormalConsulta`

## Pantallas por rol

### Recepción (UI)

Puede:

- Registrar paciente
- Ver colas y estados
- Ver prioridad asignada

No puede:

- Validar pago
- Operar consulta

### Taquilla (UI)

Puede:

- Llamar siguiente paciente
- Validar pago
- Marcar ausencia
- Reintentar llamado
- Cancelar por pago

No puede:

- Registrar pacientes
- Operar consulta

### Médico (UI)

Puede:

- Ver cola de consulta
- Llamar siguiente
- Iniciar consulta
- Finalizar consulta
- Marcar ausencia

No puede:

- Validar pago
- Registrar pacientes

## Eventos de dominio (implementados)

- `PatientCheckedIn`
- `PatientCalledAtCashier`
- `PatientPaymentValidated`
- `PatientPaymentPending`
- `PatientAbsentAtCashier`
- `PatientCancelledByPayment`
- `PatientClaimedForAttention`
- `PatientCalled`
- `PatientAbsentAtConsultation`
- `PatientAttentionCompleted`
- `PatientCancelledByAbsence`
- `ConsultingRoomActivated`
- `ConsultingRoomDeactivated`

## Endpoints por rol (runtime actual)

### Recepción (API)

- `POST /api/reception/register`
- `GET /api/reception/queue-overview/{queueId}`
- `GET /api/reception/patient-status/{queueId}/{patientId}`

Nota: actualmente los dos GET de recepción se resuelven vía endpoints de query globales (`/api/v1/waiting-room/...`).

### Taquilla (API)

- `POST /api/cashier/call-next`
- `POST /api/cashier/validate-payment`
- `POST /api/cashier/mark-absent`
- `POST /api/cashier/mark-payment-pending`
- `POST /api/cashier/cancel-payment`

### Médico (API)

- `POST /api/medical/consulting-room/activate`
- `POST /api/medical/consulting-room/deactivate`
- `POST /api/medical/call-next`
- `POST /api/medical/start-consultation`
- `POST /api/medical/finish-consultation`
- `POST /api/medical/mark-absent`

## Parámetros operativos

- Ausencia en taquilla: máximo 2 reintentos.
- Intentos de pago: máximo 3.
- Ausencia en consulta: máximo 1 reintento.
- Timeout de presencia en taquilla: 2 minutos (configurable).

## Compatibilidad con arquitectura actual

Este modelo es compatible con:

- Hexagonal Architecture
- Event Sourcing
- CQRS
- Outbox
- Idempotencia en proyecciones

## Plan de adopción por fases

### Fase 1

- Prioridad automática en registro
- Bloqueo de doble turno activo
- Separar colas de taquilla vs consulta

### Fase 2

- Flujo completo de taquilla con reintentos y cancelación por pago
- Eventos y proyecciones de taquilla

### Fase 3

- Gestión de consultorios activos
- Flujo médico completo (`LlamadoConsulta -> EnConsulta -> Finalizado`)

### Fase 4

- Endpoints y pantallas por rol
- Hardening de observabilidad y métricas operativas

## Estado actual vs target

- Estado actual: flujo clínico completo de recepción+taquilla+consulta, con consultorios activos/inactivos, transiciones estrictas y endpoints por rol.
- Brecha actual: el agregado mantiene una sola atención médica activa por cola; para paralelismo completo por múltiples consultorios se requiere extender modelo a atención activa por consultorio.
