# Testing

| Layer | Tests | Runs against | Command (from repo root) |
|---|---|---|---|
| Backend unit | 60 | in-memory domain/application objects | `cd backend && dotnet test --project tests/InventoryOps.UnitTests` |
| Architecture | 9 | compiled assemblies (NetArchTest + reflection) | `cd backend && dotnet test --project tests/InventoryOps.ArchitectureTests` |
| Backend integration | 55 | real API over HTTP + SQL Server, Redis, RabbitMQ in Testcontainers | `cd backend && dotnet test --project tests/InventoryOps.IntegrationTests` |
| Frontend component | 22 | Vitest + jsdom + Testing Library, `fetch` stubbed | `cd frontend && npm test` |
| End-to-end | 4 | Playwright (Chromium) against the running Docker stack | `cd frontend && npx playwright test --project=workflows` |

The test projects use xUnit v3 on Microsoft.Testing.Platform (`backend/global.json` opts `dotnet test` into it).

## Backend unit tests

`InventoryItemTests`, `OrderTests`, `CatalogTests` exercise the aggregates directly: every stock invariant (including a sequence of mixed operations), insufficient stock, release/fulfil limits, low-stock events firing once, decimal totals, duplicate products, mixed currencies, every allowed and forbidden status transition, SKU and money validation. `ApplicationTests` cover validators (paging limits, sort allow-list, price precision, duplicate order lines), the idempotency request hash, integration-event mapping and activity projection (including malformed payloads).

## Architecture tests

- Domain has no dependency on Application/Infrastructure/Api or on EF Core, ASP.NET Core, FluentValidation, Redis, RabbitMQ.
- Application has no dependency on Infrastructure/Api or infrastructure libraries.
- Infrastructure does not depend on Api.
- Endpoints do not depend on EF Core, persistence or aggregate types.
- Domain modules do not reference each other; application modules use other modules only through `*.Contracts`.
- Domain types expose no public setters.

## Integration tests

`InfrastructureFixture` (an xUnit assembly fixture) starts SQL Server 2022, Redis and RabbitMQ containers once, runs the production `DatabaseInitializer` (migrations, RCSI, least-privilege login, admin user) and hosts the API with `WebApplicationFactory` using the **least-privilege** connection string. `ApiClient` behaves like the browser: it keeps cookies and echoes the CSRF cookie in the header.

Tests create uniquely named data, so they run in parallel against one database. Messaging tests run in a non-parallel collection because one of them pauses the RabbitMQ container.

Highlights:

- `Concurrent_orders_never_oversell_stock`: 30 parallel orders of 1 unit against 10 units; asserts every response is 201 or 409, reserved stock equals successful orders (also cross-checked in the database), and that exactly the remaining stock can still be ordered afterwards.
- `Concurrent_fulfillment_consumes_stock_once`, `Duplicate_sku_is_rejected_even_under_concurrency`, `Concurrent_submissions_with_the_same_key_create_one_order`.
- `Database_check_constraints_back_up_the_domain_invariants`: a direct `UPDATE` violating `Reserved <= OnHand` fails with SQL error 547.
- Security: unauthenticated access, generic login errors, cookie flags, logout, missing/forged/foreign CSRF tokens, role matrix, viewer writes, privilege escalation, self-demotion, lockout, login rate limit, deactivated users, security headers.
- Cache: product details cached with TTL and removed on update; dashboard summary invalidated by stock writes; API works with Redis unreachable (readiness `Degraded`).
- Messaging: outbox → broker → activity entry with actor and processed outbox row; duplicate deliveries produce one entry and one inbox row; malformed messages land in the DLQ; with the broker paused, writes succeed and outbox rows stay pending with zero attempts, then are delivered after the broker resumes.

Requirements: Docker running locally. First run downloads the images (SQL Server ~1.5 GB).

> Windows note: on very long checkout paths (> 260 characters) `dotnet test` cannot start the test executable. Run the built assembly directly instead, e.g. `dotnet tests/InventoryOps.UnitTests/bin/Debug/net10.0/InventoryOps.UnitTests.dll`, or clone into a shorter path.

## Frontend component tests

- Product and order Zod schemas (negative/sub-cent prices, malformed SKU, duplicate lines, quantities).
- Products page: rows render reserved stock, viewers do not get write actions, loading skeleton → empty state, server errors show the correlation id and a retry button.
- Product form: client validation blocks the request; a 409 from the server is shown in the form; the request carries the CSRF header and the expected body.
- Order form: over-availability warning and total preview; idempotency key header present and **reused on retry**.
- App shell: navigation per role, permission guard, API client error mapping, session-expiry callback, CSRF token bootstrap.

## End-to-end tests

Playwright runs against `http://localhost:3000` (override with `E2E_BASE_URL`) and reads credentials from the root `.env`. Tests never delete data; they create uniquely named products and orders.

1. Manager signs in; dashboard metrics match `GET /dashboard/summary`.
2. Creates a product in the UI; verified through the API.
3. Receives 8 units; API shows on hand 8.
4. Creates an order for 3 units; API shows reserved 3, available 5.
5. Starts processing and fulfils (with confirmation dialogs); API shows on hand 5, reserved 0, order `Fulfilled`; inventory screen shows 5.
6. Viewer sees no write controls or admin navigation, is blocked from `/users`, and a crafted `POST /products` is rejected with 403.
7. Logout invalidates the session (`/auth/me` → 401, protected routes redirect to login).
8. Invalid credentials show a generic error.

Browser console errors fail the workflow tests.

```bash
docker compose up -d --build
```

```bash
cd frontend && npx playwright install chromium && npx playwright test --project=workflows
```

Screenshots for the README are produced by a separate project:

```bash
cd frontend && npx playwright test --project=screenshots
```

## What is not covered

- Load/performance testing and long-running soak tests.
- Cross-browser E2E (only Chromium).
- CI execution on GitHub-hosted runners has not happened yet (the repository had not been pushed when this was written); every CI step was executed locally on Windows.
