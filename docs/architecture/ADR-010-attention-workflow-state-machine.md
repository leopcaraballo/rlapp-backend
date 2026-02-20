# ADR-010: Attention Workflow State Machine

## Status

Accepted

## Context

El flujo clínico operativo requería soportar la secuencia real con taquilla obligatoria:

1. Recepción registra (`/api/reception/register`)
2. Taquilla llama (`/api/cashier/call-next`)
3. Taquilla valida pago (`/api/cashier/validate-payment`)
4. Médico llama siguiente (`/api/medical/call-next`)
5. Médico inicia consulta (`/api/medical/start-consultation`)
6. Médico finaliza (`/api/medical/finish-consultation`)

El bounded context `WaitingRoom` ya usa Event Sourcing + CQRS + Outbox, por lo que el nuevo flujo debía mantener:

- Inmutabilidad de eventos
- Consistencia transaccional EventStore + Outbox
- Idempotencia de proyecciones
- Compatibilidad con contratos existentes

## Decision

Se introduce una máquina de estados estricta a nivel de agregado `WaitingQueue` con estados por paciente:

Ruta principal:

- `EnEsperaTaquilla`
- `EnTaquilla`
- `PagoValidado`
- `EnEsperaConsulta`
- `LlamadoConsulta`
- `EnConsulta`
- `Finalizado`

Estados alternativos:

- `PagoPendiente`
- `AusenteTaquilla`
- `CanceladoPorPago`
- `AusenteConsulta`
- `CanceladoPorAusencia`

Eventos agregados:

- `PatientPaymentPending`
- `PatientAbsentAtCashier`
- `PatientCancelledByPayment`
- `PatientAbsentAtConsultation`
- `PatientCancelledByAbsence`
- `ConsultingRoomActivated`
- `ConsultingRoomDeactivated`

Reglas de transición:

- Prioridad alta siempre antes que normal y FIFO dentro de cada nivel.
- `EnTaquilla -> PagoValidado` habilita `EnEsperaConsulta`.
- `PagoPendiente` incrementa intentos de pago (máximo 3).
- `AusenteTaquilla` permite reintento (máximo 2).
- `LlamadoConsulta -> EnConsulta -> Finalizado`.
- `LlamadoConsulta -> AusenteConsulta` permite 1 reintento; segundo ausente cancela por ausencia.
- `medical/call-next` requiere al menos un consultorio activo.
- No se permite saltar estados ni ejecutar comandos sobre paciente no activo.

## Consequences

### Positivas

- Flujo clínico trazable de punta a punta con taquilla obligatoria
- Contratos write/read explícitos por rol (Recepción/Taquilla/Médico)
- Validaciones estrictas de transición en el agregado
- No rompe endpoints previos; se agregan endpoints operativos faltantes

### Trade-offs

- Mayor número de eventos por atención completa
- Más handlers de proyección recomendados para cubrir todos los estados alternativos
- Requiere monitoreo operativo de políticas de reintento

## Notes

La selección de `call-next` (taquilla y consulta) usa prioridad administrativa y, en empate, orden de check-in. La operación médica está acoplada a la activación/desactivación explícita de consultorios.
