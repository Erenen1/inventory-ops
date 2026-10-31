# ADR 0004: Cookie authentication with antiforgery instead of JWT

- Status: accepted
- Date: 2026-11-01

## Context

The only client is a first-party SPA served from the same origin as the API (nginx proxies `/api`). Bearer tokens kept in `localStorage` are readable by any injected script, and refresh-token rotation adds significant complexity.

## Decision

Use ASP.NET Core Identity with an HttpOnly, `SameSite=Strict` session cookie (8 h sliding expiry, `Secure` behind TLS), plus antiforgery tokens: the request token is exposed as the `XSRF-TOKEN` cookie and must be echoed in the `X-XSRF-TOKEN` header for every unsafe request, including login. Security stamps are revalidated every minute so role changes and deactivation take effect quickly. Data-protection keys are stored in SQL Server so sessions work across instances and restarts.

## Consequences

- No token is accessible to JavaScript; logout and revocation are server-side.
- CSRF protection is mandatory and implemented centrally in middleware.
- Third-party or mobile clients would need a token-based scheme in addition; that is out of scope.
