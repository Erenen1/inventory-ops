# ADR 0001: Modular monolith instead of microservices

- Status: accepted
- Date: 2026-11-01

## Context

InventoryOps serves one small-to-medium business. Its core operation — submitting an order — must validate catalog data, reserve stock for every line and create the order **atomically**. The expected team is small and the load is modest. RabbitMQ and Redis are part of the stack, which can tempt a split into services.

## Decision

Build one backend deployable (`InventoryOps.Api`) organised into modules (Catalog, Inventory, Orders, Dashboard, Activity, Identity) with:

- one SQL Server database with a schema per module,
- explicit module contracts (`*.Contracts`) for cross-module calls,
- architecture tests that enforce layer direction and module boundaries,
- background workers (outbox dispatcher, activity consumer) hosted in the same process.

## Consequences

- Order submission is a local ACID transaction; no sagas or compensations are needed for stock correctness.
- One build, one image, one deployment pipeline; simple local development.
- Modules cannot scale or deploy independently. Extraction later is possible because modules only talk through contracts and publish versioned integration events.
- A shared `DbContext` means isolation between module tables relies on conventions and tests, not on separate databases.
