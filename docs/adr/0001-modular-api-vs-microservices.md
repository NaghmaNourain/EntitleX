# ADR 0001: Modular control-plane API vs microservices

- **Status:** Accepted
- **Date:** 2026-11-03
- **Deciders:** EntitleX portfolio architecture (Phase 1)

## Context

EntitleX is an enterprise license-extension platform (portfolio / fictional CloudShield and NetworkGuard vendors). Phase 1 must establish foundations that can grow into a credible control plane without premature distributed-system complexity.

Two candidate shapes were considered:

1. **Many fine-grained microservices** from day one (requests API, approvals API, entitlements API, etc.), each with its own deployable and data store.
2. **Modular control-plane API** (single deployable host) with clear internal module boundaries, plus **separate worker processes** and **vendor simulator APIs**.

## Decision

Adopt a **modular control-plane API** (`EntitleX.Api`) as the synchronous entry point, with:

- **Clean Architecture** layering: Domain → Application → Infrastructure → Api
- **Selective DDD**: bounded contexts and aggregates where complexity justifies them; avoid ceremony for simple CRUD
- **Separate workers** for asynchronous work: fulfillment, outbox publishing, reconciliation, notifications
- **Separate simulator hosts** for CloudShield and NetworkGuard (each with their own Clean Architecture skeleton)
- **Lambda stubs** for scheduled reminder/expiration jobs (deployed independently later)

Do **not** split the control plane into many independently deployed microservices in early phases.

## Consequences

### Positive

- Faster local iteration: one primary API to run and debug
- Clear module seams for future extraction without rewriting use-cases
- Workers and simulators remain independently scalable/restartable where async and isolation matter
- Clean Architecture keeps domain/application free of infrastructure coupling from the start

### Negative / trade-offs

- Control-plane modules share a process; noisy-neighbor and blast-radius concerns must be managed with discipline (and later, optional extraction)
- Team must enforce module boundaries via folders/projects and reviews rather than network boundaries
- Outbox + messaging become important early for reliable async decoupling (Phase 2+)

### Follow-ups (later phases)

- Introduce domain aggregates, EF Core, outbox, and Kafka producers/consumers
- Extract a module to its own service only when ownership, scale, or failure isolation demands it
