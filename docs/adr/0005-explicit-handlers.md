# ADR 0005: Explicit use-case handlers and read stores, no mediator library

- Status: accepted
- Date: 2026-11-01

## Context

Clean-architecture templates often add MediatR (or similar) with pipeline behaviours for every request, plus a generic repository over EF Core.

## Decision

- Endpoints call small, explicitly injected handler classes (`SubmitOrderHandler`, `OrderWorkflow`, `InventoryCommands`, …). Validation is invoked inside handlers; transactions go through `IUnitOfWork`.
- Reads go through projection-only read-store ports (`IProductReadStore`, `IOrderReadStore`, …) implemented with EF Core `AsNoTracking` projections.
- Aggregates are loaded through small, purpose-specific repositories rather than a generic `IRepository<T>`.

## Consequences

- "Go to definition" from an endpoint leads straight to the code that runs; there is no reflection-based dispatch.
- Cross-cutting concerns (validation, transactions, cache invalidation) are visible in each handler instead of hidden in pipeline behaviours; a little repetition is accepted.
- Reads and writes are separated without a CQRS framework.
