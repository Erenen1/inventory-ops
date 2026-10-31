# ADR 0002: Optimistic concurrency with bounded retries for stock

- Status: accepted
- Date: 2026-11-01

## Context

Concurrent orders for the same product must never oversell (`Reserved <= OnHand`, `Available >= 0`). Options considered:

1. **Atomic conditional updates** (`UPDATE … SET Reserved = Reserved + @q WHERE OnHand - Reserved >= @q`): fast and lock-light, but moves the business rule out of the domain model into SQL, and multi-line orders still need a transaction plus rollback handling.
2. **Pessimistic locking** (`UPDLOCK`/`SERIALIZABLE` reads): correct, but holds locks for the whole use case, increases deadlock risk and couples handlers to SQL hints.
3. **Optimistic concurrency** (`rowversion` + retry): rules stay in `InventoryItem`; conflicts are detected at commit.

## Decision

Use option 3. `InventoryItem`, `Order` and `Product` have `rowversion` tokens. `EfUnitOfWork` runs each use case in a transaction and, on `DbUpdateConcurrencyException` or SQL deadlock 1205, discards tracked state and re-runs the whole operation with jittered exponential backoff, up to 6 attempts, then returns `409 concurrency_conflict`. Multi-line reservations are applied in product-id order. SQL check constraints back up the invariants. The database uses `READ_COMMITTED_SNAPSHOT` so readers do not block writers.

## Consequences

- An integration test submits 30 concurrent single-unit orders against 10 units: reserved stock always equals the number of successful orders and no oversell occurs.
- Under extreme contention on a single SKU some requests can exhaust retries and receive 409 even though stock remained; clients may retry (the order form keeps its idempotency key). If contention became a real problem, option 1 could be introduced for the reservation step only.
- Use cases must be re-runnable and must not perform side effects inside the transaction delegate (cache invalidation happens after commit).
