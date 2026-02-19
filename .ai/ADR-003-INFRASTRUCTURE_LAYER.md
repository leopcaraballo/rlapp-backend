# ADR-003: Infrastructure Layer with PostgreSQL + RabbitMQ + Outbox

**Date:** 2026-02-19
**Status:** ACCEPTED
**Authors:** Architecture Team
**Related:** PHASE-3 Implementation, ADR-006 Outbox Pattern

## Context

The RLAPP system needs a concrete Infrastructure layer that:

1. Persists domain events as the source of truth
2. Publishes events to external subscribers reliably
3. Supports CQRS read models without leaking infra to Domain/Application
4. Enforces idempotency and optimistic concurrency
5. Keeps infra swappable without touching Domain/Application

## Decision

Implement Infrastructure with:

- **Event Store:** PostgreSQL table as the primary event source
- **Outbox:** PostgreSQL outbox table for reliable publication
- **Broker:** RabbitMQ for integration event delivery
- **Serialization:** Newtonsoft.Json for event payloads

### Core Rules

- Application depends only on ports (`IEventStore`, `IEventPublisher`)
- Infrastructure implements those ports
- Event store and outbox writes are atomic
- Publishing is decoupled and retryable
- Domain remains pure and isolated

## Consequences

### Positive

1. Infrastructure is replaceable
2. Event sourcing is durable and auditable
3. Reliable publish via outbox
4. Clear boundaries between layers
5. Ready for scaling and replay

### Negative / Tradeoffs

1. Additional tables and operational overhead
2. Requires background processing for outbox
3. More moving parts than direct publish

## Alternatives Considered

1. Direct broker publish only (rejected: risk of data loss)
2. No broker (rejected: no integration capability)
3. EventStoreDB (postponed: not required yet)

## Notes

- Outbox status is updated after successful publish
- Failed publish remains pending for retry
- Read models use separate storage (PostgreSQL)
