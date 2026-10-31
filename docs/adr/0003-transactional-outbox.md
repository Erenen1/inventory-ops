# ADR 0003: Transactional outbox for integration events

- Status: accepted
- Date: 2026-11-01

## Context

Business changes must produce events (activity feed today; notifications and integrations later). Publishing to RabbitMQ inside or right after the SQL transaction can lose events (crash after commit) or publish events for rolled-back changes (publish before commit). Distributed transactions are not an option.

## Decision

- Domain events are collected on aggregates and converted to versioned integration events in `InventoryOpsDbContext.SaveChanges`, which inserts `messaging.OutboxMessages` rows in the **same transaction** as the business data.
- `OutboxDispatcher` (hosted service) claims pending rows with `UPDLOCK, READPAST` and a lease, publishes persistent `mandatory` messages with publisher confirms, then marks them processed.
- Broker outages leave rows pending without consuming attempts; per-message failures back off exponentially and are parked after 10 attempts.
- Consumers acknowledge manually and deduplicate via `messaging.InboxMessages`, written in the same transaction as their side effects; malformed messages are dead-lettered; quorum queues cap redeliveries.

## Consequences

- No lost or phantom events; delivery is at-least-once and consumer effects are idempotent. Exactly-once delivery is not claimed.
- Events lag the transaction by the polling interval (about one second).
- Ordering across messages is not guaranteed; consumers must be order-insensitive.
- Outbox and inbox tables grow and need a retention job (not implemented yet); parked messages need manual replay.
