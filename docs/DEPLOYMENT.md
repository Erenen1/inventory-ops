# Deployment and local environment

## Local stack (Docker Compose)

| Service | Image | Network | Published | Health check |
|---|---|---|---|---|
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-CU27-ubuntu-22.04` (Developer Edition) | backend (internal) | – | `sqlcmd SELECT 1` |
| `redis` | `redis:7.4.12-alpine` (password, AOF) | backend | – | `redis-cli ping` |
| `rabbitmq` | `rabbitmq:4.1.8-management-alpine` | backend | – | `rabbitmq-diagnostics check_port_connectivity` |
| `migrator` | `inventoryops-api:local` (`migrate` command) | backend | – | exits 0 on success |
| `api` | `inventoryops-api:local` | backend + edge | – | `dotnet InventoryOps.Api.dll healthcheck` → `/api/health/ready` |
| `frontend` | `inventoryops-frontend:local` (nginx-unprivileged) | edge | `127.0.0.1:3000` | `wget /healthz` |

Startup order is enforced with health-based `depends_on`: infrastructure healthy → migrator completed successfully → API healthy → frontend. Stateful services use named volumes (`sqlserver-data`, `redis-data`, `rabbitmq-data`); RabbitMQ has a fixed hostname so its node name (and durable queues) survive container re-creation.

The `backend` network is `internal: true`: SQL Server, Redis and RabbitMQ have no published ports and no route to the outside. `compose.dev.yaml` optionally exposes them on `127.0.0.1` for IDE debugging.

### Environment variables (`.env`)

| Variable | Used by | Purpose |
|---|---|---|
| `SQL_SA_PASSWORD` | sqlserver, migrator | SQL Server administrator (migrations and login provisioning only) |
| `SQL_APP_PASSWORD` | migrator, api | password of the least-privilege `inventoryops_app` login |
| `REDIS_PASSWORD` | redis, api | Redis `requirepass` |
| `RABBITMQ_PASSWORD` | rabbitmq, migrator, api | broker user `inventoryops` |
| `ADMIN_EMAIL`, `ADMIN_PASSWORD` | migrator | first administrator (created once) |
| `SEED_DEMO_DATA`, `DEMO_*` | migrator | opt-in demo dataset and demo users |
| `FRONTEND_BIND`, `FRONTEND_PORT` | frontend | published address (default `127.0.0.1:3000`) |
| `REQUIRE_HTTPS_COOKIES` | api | `Secure` cookie flag; set `true` behind TLS |
| `OPENAPI_ENABLED` | api | expose `/api/docs` and the OpenAPI document |

Changing `SQL_SA_PASSWORD` after the first start has no effect on an existing volume (SQL Server keeps the original password); reset with `docker compose down -v` (destroys local data) or change it inside SQL Server.

### CPU architecture

The SQL Server Linux image is published for `linux/amd64` only. On Apple Silicon, Docker Desktop runs it through emulation (Rosetta), which works but is slower; alternatively point `ConnectionStrings__Database`/`DatabaseAdmin` at Azure SQL Edge or a remote SQL Server. All other images are multi-arch.

### Resource usage

SQL Server needs about 2 GB of memory. Give Docker Desktop at least 4 GB.

## Images

- **API** (`backend/Dockerfile`, build context = repository root so the shared `.editorconfig` drives analyzers): SDK 10.0.403 build stage with locked restore → `aspnet:10.0.14-noble-chiseled-extra` runtime (no shell, non-root `app` user, ICU included for SqlClient). The same image serves the API, the `migrate` command and the container health probe.
- **Frontend** (`frontend/Dockerfile`): `node:24.22.2-alpine` build with `npm ci` → `nginxinc/nginx-unprivileged:1.30.6-alpine` serving `dist/` on port 8080.

## Production considerations (not implemented)

This repository contains no production deployment. A real deployment would at least:

- terminate TLS in front of nginx (or an ingress/load balancer) and set `REQUIRE_HTTPS_COOKIES=true`, `OPENAPI_ENABLED=false`, `SEED_DEMO_DATA=false`;
- use a licensed SQL Server edition or Azure SQL, managed Redis and RabbitMQ (or a hosted broker), each with TLS;
- provide secrets from a secret store, not a `.env` file;
- run migrations as a release step (`migrate` command or an EF migration bundle) before deploying new API instances;
- configure `ForwardedHeaders:KnownNetworks` for the actual proxy network;
- ship logs to a central store and add OpenTelemetry traces and metrics;
- scan images and add a retention job for outbox/inbox rows.

Running several API instances is supported by the design: outbox dispatch uses row leases with `READPAST`, consumers share the quorum queue, sessions are cookie-based, and ASP.NET Core data-protection keys (which encrypt cookies and antiforgery tokens) are stored in SQL Server (`identity.DataProtectionKeys`), so every instance can read every session and sessions survive container re-creation. The keys are not encrypted at rest yet; production should add `ProtectKeysWithCertificate` or a key-vault key.
