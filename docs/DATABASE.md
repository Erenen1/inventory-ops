# Database

SQL Server 2022, one database (`InventoryOps`), one schema per module. EF Core 10 maps the model with explicit `IEntityTypeConfiguration` classes in `backend/src/InventoryOps.Infrastructure/Persistence/Configurations`.

## Tables

| Schema.Table | Key | Notes |
|---|---|---|
| `catalog.Categories` | `Id` (GUID v7) | unique `Name` |
| `catalog.Products` | `Id` (GUID v7) | unique `Sku` (varchar 32), `UnitPrice decimal(12,2)` + `Currency char(3)` (complex type `Money`), `IsActive`, `rowversion`, check `UnitPrice >= 0`, FK → Categories (restrict) |
| `inventory.InventoryItems` | `ProductId` (1:1 with product) | `OnHand`, `Reserved`, `ReorderThreshold`, `rowversion`; checks `OnHand >= 0`, `Reserved >= 0`, `Reserved <= OnHand`, `ReorderThreshold >= 0` |
| `inventory.StockMovements` | `Id bigint identity` | append-only: type, deltas, resulting on-hand/reserved, reason, note, order id, actor, timestamp |
| `orders.Orders` | `Id` (GUID v7, **non-clustered PK**) | **clustered unique** `Number` (from sequence `orders.OrderNumbers`, starts at 100001), status, currency, `TotalAmount decimal(14,2)`, `CreatedBy`, idempotency key + request hash, `rowversion` |
| `orders.OrderLines` | `Id` | price and name snapshots, `Quantity > 0`, unique `(OrderId, ProductId)`, FK → Products (restrict), cascade from order |
| `orders.OrderStatusHistory` | `Id bigint identity` | from/to status, actor, reason, timestamp |
| `activity.ActivityEntries` | `Id bigint identity` | unique `SourceMessageId`, module, severity, summary, subject id, actor |
| `messaging.OutboxMessages` | `Id bigint identity` | unique `MessageId`, type, JSON payload, attempts, next attempt, lease, processed / dead-lettered timestamps, last error |
| `messaging.InboxMessages` | `(MessageId, Consumer)` | processed-message record per consumer |
| `identity.*` | ASP.NET Core Identity | users (with `DisplayName`, `IsActive`), roles, claims, logins, tokens, and `DataProtectionKeys` (shared cookie/antiforgery key ring) |

## Relationships and delete behaviour

- Products are **never deleted** (deactivated instead). All foreign keys to products are `RESTRICT`, so history cannot disappear by accident.
- Order lines and status history cascade from their order (orders themselves are not deleted by the application).
- Inventory items are created together with their product (`IInventoryProvisioning`).

## Indexes

| Index | Serves |
|---|---|
| `Products(Sku)` unique, `Products(Name)`, `Products(CategoryId, IsActive)` | SKU lookup/uniqueness, name sort, category/status filters |
| `Orders(Number)` unique clustered | order-number search and default "newest first" ordering, sequential inserts |
| `Orders(CreatedBy, IdempotencyKey)` unique | idempotent submission |
| `Orders(Status, CreatedAt)`, `Orders(CreatedAt)` | status filter, dashboard date ranges |
| `OrderLines(ProductId)` | stock/order cross-reference |
| `StockMovements(ProductId, OccurredAt DESC)`, filtered `StockMovements(OrderId)` | movement history paging, order traceability |
| `OutboxMessages(NextAttemptAt, Id)` filtered on `ProcessedAt IS NULL AND DeadLetteredAt IS NULL` | dispatcher polling touches only pending rows |
| `ActivityEntries(OccurredAt DESC)`, `(Module, OccurredAt DESC)`, unique `SourceMessageId` | feed paging, module filter, duplicate protection |

Free-text search uses `LIKE '%term%'` (via EF Core `Contains`) on name/SKU, which cannot use an index seek; at the intended catalog size (thousands of products) this is acceptable. Full-text search is a future option.

## Identifiers

Domain objects generate GUID v7 ids, so aggregates have identity before they are saved and ids reveal no row counts. SQL Server sorts `uniqueidentifier` by its last bytes, so v7 GUIDs are **not** sequential for SQL Server; high-volume tables therefore avoid a clustered GUID key (orders cluster on `Number`; logs use `bigint identity`). Low-volume tables (categories, products) keep clustered GUID keys.

## Concurrency control

`rowversion` columns (EF shadow properties) on `Products`, `InventoryItems` and `Orders` implement optimistic concurrency. Combined with the retrying unit of work this guarantees that a reservation or transition is always decided against current data. See [ARCHITECTURE.md](ARCHITECTURE.md#transactions-and-concurrency).

## Transaction isolation

`READ_COMMITTED_SNAPSHOT ON` is set by the migrator. Default isolation remains READ COMMITTED; with row versioning, dashboards and lists do not block on writers. No query changes the isolation level.

## Migrations

- Migrations live in `Infrastructure/Persistence/Migrations` (generated code, excluded from analyzers).
- Create a migration (from `backend/`):

  ```bash
  dotnet tool restore
  ```

  ```bash
  dotnet ef migrations add <Name> --project src/InventoryOps.Infrastructure --startup-project src/InventoryOps.Api --output-dir Persistence/Migrations
  ```

- Apply: the API image's `migrate` command (the `migrator` Compose service) runs `Database.MigrateAsync()` with an elevated connection (`ConnectionStrings__DatabaseAdmin`), then enables RCSI, provisions the runtime login (`db_datareader`, `db_datawriter`, `UPDATE` on the order-number sequence), ensures roles, bootstraps the first admin and optionally seeds demo data. Every step is idempotent; running it twice is safe.
- The API process never migrates on startup. In production, run the same command (or an EF migration bundle / reviewed SQL script generated with `dotnet ef migrations script --idempotent`) as a release step before rolling out new API instances.

## Seed data

`DemoDataSeeder` creates 5 categories, 24 products, opening stock and 14 orders in all four statuses, through the domain model (so stock movements, reservations and outbox events are real). It runs only when `Bootstrap:SeedDemoData=true` and only if the catalog is empty, so repeated runs never duplicate data. Timestamps are relative to the seeding date; quantities and names are fixed. Demo users are created only in the same opt-in mode.
