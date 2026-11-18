# SupplyForge

Modular-monolith backend for the full goods lifecycle — supplier → purchase →
receipt → stock → sale → shipment → invoice → payment → report. One company,
three seeded branches. This is a **portfolio backend, not a certified ERP**
(see `docs/prd/01-introduction.md`).

**Status:** active development — slice **A0** in progress (increment A0-6).
Slice-by-slice progress with evidence lives in
[`docs/A0-PROGRESS.md`](docs/A0-PROGRESS.md); session continuity in
[`HANDOFF.md`](HANDOFF.md).

## Stack

- C# / .NET 10, ASP.NET Core Web API, EF Core 10, SQL Server 2022
- xUnit: unit tests + integration tests against a real SQL Server, plus
  architecture tests
- Docker Compose (SQL Server), GitHub Actions CI, OpenAPI generated from code

## Architecture overview

A **modular monolith**: one deployable Host process, modules that own their
tables. A conceptual diagram (`SF.png`) is planned but not yet in the
repository.

| Area | Location | Owns |
|---|---|---|
| Platform Core | `src/Modules/PlatformCore/` | users, roles, permissions, branch/warehouse scope, audit, notifications, API keys, auth |
| Integrations | `src/Modules/Integrations/` | outbox / integration-adjacent tables |
| Shared kernel | `src/Shared/` | cross-cutting pieces only (auditing interfaces, idempotency, sequencing, notifications contracts) |
| Host | `src/Host/SupplyForge.Host/` | HTTP endpoints, auth wiring, DI composition, middleware |

Rules enforced by architecture tests (NFR-040, `tests/ArchitectureTests/`):

- No module reads or writes another module's tables; cross-module reads go
  through public query contracts, writes through public commands or events
  published to the outbox in the same transaction (MOD-1…5).
- Only Inventory posts stock movements; only Finance owns amounts owed/paid
  and credit exposure; Sales never reads Finance tables (MOD-9).
- Reporting is event-only and owns no operational writes (MOD-6).
- Per module: Domain → Application ← Infrastructure, endpoints in Host only;
  EF entities are never exposed through the API.

Authentication (ADR-006/009): interactive users send
`Authorization: Bearer <access-token>` (JWT, ADR-004); Integration Clients
send the frozen `X-Api-Key: <secret>` header. Authorization is **deny by
default**: every request re-authorizes (SEC-10), endpoints declare named
`perm:*` policies (PLT-022), and unauthenticated/under-permitted callers get
401/403 exactly (SEC-13).

## Development progress

Slice-by-slice detail with full evidence: [`docs/A0-PROGRESS.md`](docs/A0-PROGRESS.md).

| Slice | What it delivered | Status |
|---|---|---|
| M0-0…M0-5 | Solution, Docker Compose (SQL Server), per-module DbContexts + initial migrations, platform services, Host skeleton, smoke tests | Done |
| A0-1 | JWT access tokens (ADR-004): standard JwtBearer handler, HS256, 15-min TTL, issuer/audience/skew validation | Done |
| A0-2 / A0-2b | Rate limiting on auth endpoints (SEC-04, PLT-012) plus in-step defect fixes (bounded limiter cardinality, bounded login-body buffering) | Done |
| A0-3 | Permission catalogue, role bundles, deny-default endpoint authorization (PLT-020/022), secure create-user bootstrap under `sp_getapplock`, complete HTTP 401/403 matrix (SEC-13) | Done (9/9 steps) |
| A0-4 | Branch-isolation HTTP matrix (BRN-011/013/014), audit-log redaction (PLT-044), OpenAPI dual-auth schemes (INT-052). PLT-013 (password reset) deferred — needs migration approval | Done (PLT-013 deferred) |
| A0-5 | GitHub Actions CI (build → format → migrations → tests → container build), `Dockerfile`, this README (NFR-061/062) | Done |
| A0-6 | ADR-003 ambient transaction: `IDbSession` sharing one `DbConnection`/`DbTransaction` across both module DbContexts, session-then-email notification raise (MOD-TXN-02), ADR-003 "how tests prove it" table | **In progress** — S1–S3 core done (6 new tests green, incl. a root-cause fix for a concurrent idempotency-claim race); S3 HTTP round-trip test, S4–S6 remain |

**Last verified sweep (2026-11-18):** `dotnet build` 0 errors / 0 warnings ·
`dotnet test` **147/147** (14 architecture + 59 unit + 74 integration against
real SQL Server) · `dotnet format --verify-no-changes` clean.

### What remains

- **A0-6 remainder:** HTTP round-trip test for `POST /api/v1/notifications`
  through the real Host DI; adopt `IDbSessionFactory` in
  `AuthService.CreateUserAsync` (S4); verification sweep (S5); close-out
  notes and throwaway spike cleanup (S6).
- **A0 gate review:** sign-in flow, branch-isolation tests, notification
  round-trip, API-key scope test, first CI run green on GitHub.
- **Then slice A1** (Master data + Inventory core). Parked on owner
  decisions: PLT-013 password reset (migration approval), BRN-012/SEC-12
  foreign-branch 404 leg (first branch-owned resource in A1), NFR-060
  compose `app` service, IDEM-04 HTTP idempotency mapping (first endpoint
  that accepts an idempotency key).

## Repository map

| Path | What |
|---|---|
| `docs/prd/` | Requirements — authority for behavior; start at `docs/prd/INDEX.md` |
| `ADR/`, `docs/Decision Register.md` | Accepted decisions |
| `docs/A0-PROGRESS.md` | Increment log: what was done, evidence, defects |
| `HANDOFF.md` / `docs/HANDOFF.md` | Current-state handoff (kept byte-identical) |
| `AGENTS.md` | Working agreements for AI-assisted development |
| `.github/workflows/ci.yml` | CI definition (NFR-061) |

## Runbook

### Prerequisites

.NET 10 SDK, Docker with Compose.

### 1. Start SQL Server

```bash
cp .env.example .env   # first time; adjust MSSQL_SA_PASSWORD if needed
docker compose up -d sqlserver
```

### 2. Run the host

```bash
dotnet run --project src/Host/SupplyForge.Host
```

The host seeds the permission catalogue and system role bundles at startup
(idempotent; no migration involved).

### 3. First-run sign-in

No users exist on a fresh database. `POST /api/v1/users` is open **only while
the Users table is empty** (bootstrap window); afterwards it requires an
authenticated caller with `platform.user.manage`:

```bash
# create the first user (bootstrap)
curl -X POST http://localhost:5009/api/v1/users \
  -H 'Content-Type: application/json' \
  -d '{"userName":"admin","email":"admin@example.test","password":"<strong-pw>","displayName":"Admin"}'

# sign in
curl -X POST http://localhost:5009/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"userName":"admin","password":"<strong-pw>"}'
# → { "userId", "accessToken", "refreshToken", "accessTokenExpiresAt" }
```

Refresh tokens rotate on use (PLT-011). Login/refresh are rate-limited per IP
and per account (SEC-04/PLT-012).

### 4. Configuration and secrets

`appsettings.json` carries **development placeholders only** (SEC-20). Real
secrets arrive via environment variables, e.g.
`ConnectionStrings__SupplyForge`, `Auth__AccessTokenSecret`,
`RateLimit__AuthIpLimit`.

### 5. Migrations (ask first)

Migrations change module-owned schema and require owner approval before
running (project rule). When approved:

```bash
dotnet ef database update \
  --project src/Modules/PlatformCore/SupplyForge.PlatformCore.Infrastructure \
  --startup-project src/Host/SupplyForge.Host
dotnet ef database update \
  --project src/Modules/Integrations/SupplyForge.Integrations.Infrastructure \
  --startup-project src/Host/SupplyForge.Host
```

CI applies both to its own ephemeral database automatically.

### 6. Tests and format

```bash
dotnet test    # needs the compose SQL Server running (shared dev database)
dotnet format --verify-no-changes
```

`dotnet test` runs architecture tests (NFR-040), unit tests, and integration
tests against real SQL Server (NFR-050). The integration tests connect with
the default dev password from `.env.example`; if you change it, update the
test connection strings too.

### Operations note: rate limiting

Rate-limit buckets are in-memory **per instance** (MOD-8: no Redis). Behind
multiple instances the effective limit multiplies; database lockout after
repeated failures is the cross-instance brake (PLT-012).

## Generated OpenAPI (INT-052, NFR-062)

The OpenAPI document is generated from code. In the **Development**
environment it is served at:

```
GET http://localhost:5009/openapi/v1.json
```

It documents both auth schemes (`Bearer` for interactive users,
`X-Api-Key` for Integration Clients — ADR-006/009), per-endpoint security
requirements derived from endpoint authorization metadata, and the error
problem-details shape.

## Docker image (NFR-061)

```bash
docker build -t supplyforge-host .
docker run -p 8080:8080 \
  -e "ConnectionStrings__SupplyForge=Server=<host>,1433;Database=SupplyForge;User Id=sa;Password=<pw>;TrustServerCertificate=True;Encrypt=True" \
  supplyforge-host
```

Only build arguments are code; configuration (connection strings, JWT
settings, limits) is injected at run time, never baked into the image.

## CI (NFR-061)

Every PR runs `.github/workflows/ci.yml`: build → format check → migrations
against an ephemeral SQL Server service container → full test suite
(including architecture tests) → container build.

## Documentation pointers

- Requirements: [`docs/prd/INDEX.md`](docs/prd/INDEX.md) (navigation; part
  files win over the index)
- Decisions: [`ADR/`](ADR/), [`docs/Decision Register.md`](docs/Decision%20Register.md)
- Engineering agreements: [`AGENTS.md`](AGENTS.md)
