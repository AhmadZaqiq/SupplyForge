# SupplyForge

Modular-monolith backend for the full goods lifecycle — supplier → purchase →
receipt → stock → sale → shipment → invoice → payment → report. One company,
three seeded branches. This is a **portfolio backend, not a certified ERP**
(see `docs/prd/01-introduction.md`).

**Status:** active development — slice **A0**: increments A0-1…A0-7 done; the **A0 gate is closed** (all five PRD §11.2 items evidenced in `docs/A0-PROGRESS.md`). Slice **A1**: A1-1, A1-2, A1-3 committed; **A1-4 Part 1 committed (93b28c0)** — Warehouses and Storage Locations; next: A1-4 Part 2 after Q-E (branch scope).

Slice-by-slice progress with evidence lives in
[`docs/A0-PROGRESS.md`](docs/A0-PROGRESS.md) and [`docs/A1-PROGRESS.md`](docs/A1-PROGRESS.md); session continuity in
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
| **Master Data** | `src/Modules/MasterData/` | **company, branches, product catalogue, partners, tax rates, payment terms, reason codes, warehouses, storage locations** |
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

## Implemented functionality

### Slice A0 — Foundations (committed, all gates closed)

| Increment | Delivered |
|---|---|
| M0-0…M0-5 | Solution, Docker Compose (SQL Server), per-module DbContexts + initial migrations, platform services, Host skeleton, smoke tests |
| A0-1 | JWT access tokens (ADR-004): standard JwtBearer handler, HS256, 15-min TTL, issuer/audience/skew validation |
| A0-2 / A0-2b | Rate limiting on auth endpoints (SEC-04, PLT-012) plus in-step defect fixes (bounded limiter cardinality, bounded login-body buffering) |
| A0-3 | Permission catalogue, role bundles, deny-default endpoint authorization (PLT-020/022), secure create-user bootstrap under `sp_getapplock`, complete HTTP 401/403 matrix (SEC-13) |
| A0-4 | Branch-isolation HTTP matrix (BRN-011/013/014), audit-log redaction (PLT-044), OpenAPI dual-auth schemes (INT-052). PLT-013 (password reset) deferred — needs migration approval |
| A0-5 | GitHub Actions CI (build → format → migrations → tests → container build), `Dockerfile`, this README (NFR-061/062) |
| A0-6 | ADR-003 ambient transaction: `IDbSession` sharing one `DbConnection`/`DbTransaction` across module DbContexts, session-then-email notification raise (MOD-TXN-02), in-app notification read API, HTTP notification round-trip, `AuthService` create-user on the ambient session |
| A0-7 | Notification delivery state (ADR-008 legs 4–5): `Email` config (`Fake`/`Disabled`, validated at startup), delivery columns + approved migration, inline post-commit attempt recording Sent/Pending/Failed, retry worker with exponential backoff + jitter → dead-letter `Failed` (PLT-052, INT-003), visible pending when email is off (PLT-051), audited TD-2 manual mark-sent endpoint, delivery status on the read API |
| A0-8 | Hardening: SEC-20 startup guard fails outside Development, PLT-073b `NextAsync` via compare-and-swap, SEC-05/PLT-012 password verified before `user_inactive`/`locked_out`, PLT-011 refresh returns real `userId`, OUT-01/OUT-06 `IOutbox` correlationId + active-transaction guard + `IInbox` fixed, MD-004 README accuracy |

### Slice A1 — Master Data + Inventory Core

| Increment | Delivered | Status |
|---|---|---|
| A1-1 | `MasterData` module per ADR-001, `master_data` schema + company singleton + 3-branch seed (MD-001/MD-002), ambient session generalised 2 → N contexts (ADR-003), MOD-10 arch-test extension | **Committed** (`704b665`) |
| A1-2 | Product catalogue — `Product`/`ProductStatus`/`Category`/`UnitOfMeasure`/`UnitOfMeasureConversion` (MD-010…MD-013), zero-cascade FKs, CHECK `Factor > 0` | **Committed** (`704b665`) |
| A1-3 | Partners + shared master — `Supplier`/`Customer` + statuses, `CreditLimit` decimal(18,2) NOT NULL default 0 (CHECK ≥ 0), addresses with default-shipping/billing filtered unique indexes, `TaxRate` (Rate decimal(9,6), CHECK 0–100), `PaymentTerm` (DueDays CHECK ≥ 0), `ReasonCode` global list (MD-020, MD-030, MD-031, MD-050, PLT-075) | **Committed** (`708d171`) |
| **A1-4 Part 1** | **Warehouses + Storage Locations** — `Warehouse` (branch-owned, `Code` unique company-wide non-filtered), `StorageLocation` (7 location types per D-16: Receiving, Storage, Picking, Shipping, Returns, Quarantine, Blocked), `LocationType` enum, port `ILocationDeactivationGuard` + placeholder implementation, migration file `20261124181017_A1WarehousesLocations`, 7 unit + 12 integration tests on throwaway DBs | **Committed** (`93b28c0`) |
| A1-4 Part 2 | Branch scope via `ICurrentBranchScope` + EF global query filters on `MasterDataDbContext` (BRN-001…014) | **Planned** (blocked on Q-E: null scope behavior) |

### Master Data capabilities implemented so far

- **Company** — singleton legal entity with profile fields
- **Branches** — 3 seeded branches (placeholder codes/names per Q-3)
- **Product catalogue** — products with code, status lifecycle (Active/Inactive/Archived), category hierarchy, UoM with conversions
- **Partners** — suppliers and customers with statuses, credit limits, addresses (default shipping/billing)
- **Shared lookups** — tax rates, payment terms, reason codes
- **Warehouses & Storage Locations** — branch-owned warehouses, 7 location types, block location (no deactivate while holding stock)

## Implemented but not yet applied database migrations

| Migration | Module | Tables | Status |
|---|---|---|---|
| `20261124181017_A1WarehousesLocations` | MasterData | `Warehouse`, `StorageLocation` | **File only** — `dotnet ef database update` requires explicit owner approval |

CI applies all migrations to its own ephemeral database automatically.

## Planned functionality (not implemented)

### Slice A1 (continued)
- A1-5: Inventory ledger + balances (`StockMovement`, `StockBalance`, reconciliation)
- A1-6: WAC core (bucket = ProductId+WarehouseId, opening blend, receipt blend, issues, transfers)
- A1-7: Documents that post movements (adjustments, transfers, opening import)
- A1-8: Availability + replenishment + returns guard
- A1-9: Admin user lifecycle (role/branch assignment, last-admin protection)

### Slice A2 — Buy & Receive
- Procurement (purchase orders, receipt processing)
- Inbound (goods receipt, inspection)

### Slice A3 — Sell & Reserve
- Sales (quotes, orders, reservations)

### Slice A4 — Fulfil & Dispatch
- Fulfillment (pick/pack)
- Shipping (dispatch, shipment tracking)

### Slice B1+ — Money & Reporting
- Finance (invoices, payments, AP/AR)
- Reporting (event-sourced insights)

## Important architectural and engineering decisions

| ID | Decision |
|---|---|
| ADR-001 | Modular monolith, leaf contract assemblies per module, single solution |
| ADR-002 | Event versioning, consumer-owned DTOs, no broker |
| ADR-003 | Ambient transaction + outbox in same TXN, per-module DbContexts sharing one connection |
| ADR-004 | Identity: PBKDF2, JWT access (15 min), refresh rotation (14 days), lockout, scope freshness 5 min |
| ADR-005 | Document numbering/sequences, gaps allowed |
| ADR-006 | API surface: `/api/v1`, ProblemDetails, pagination, optimistic concurrency, idempotency keys |
| ADR-008 | Notifications + email fake (TD-2: manual mark-sent, email optional in P1) |
| ADR-009 | API keys with scopes, `X-Api-Key` header frozen |
| PA-18 (DM-3) | **Guid surrogate keys** are the project key style (owner accepted 2026-11-24) |
| MOD-1/1a | Modules own tables; cross-module via contracts/events; no cross-schema FKs |
| BRN-010 | Users assigned one+ branches or company-wide scope flag (`IsCompanyWide` on `platform.User`) |
| Q-A…Q-D | A1-4 decisions: `Warehouse.Code` unique company-wide, `StorageLocation` entity name, `IsCompanyWide` Part 2 only, `ICurrentBranchScope` = BranchIds + IsCompanyWide + CanAccess |
| Q-E | **Open**: null branch scope fails closed for requests; seed/migration retain access |

## Current implementation progress

| Area | Status |
|---|---|
| **A0 gate** | **Closed** — all five PRD §11.2 items evidenced |
| **A1-1, A1-2, A1-3** | **Committed** — 32 tables across 3 schemas (14 platform, 15 master_data, 3 integrations) |
| **A1-4 Part 1** | **Committed** (`93b28c0`) — Warehouses + Locations entities, migration file, 19 tests |
| **A1-4 Part 2** | **Planned** — branch scope (blocked on Q-E) |
| **A1-5…A1-9** | **Planned** — per `docs/A1-PROGRESS.md` §1 |
| **Owner decisions open** | ADR-007 (approval engine), ADR-011 (logo upload), OD-03/TD-5 (WAC reversal), PLT-013 (password reset migration), Q-5 (email in P1), Q7 (MD-003 placement) |

## Repository maintenance improvements (this commit)

- **.gitignore** — added `**/bin/` and `**/obj/` patterns for build artifacts
- **Script documentation** — `scripts/README.md` documents all three scripts (`quota-status.sh`, `checkpoint.sh`, `checkpoint-selftest.sh`) with arguments, behavior, prerequisites, and exit codes
- **Plan lifecycle** — `.kilo/plans/README.md` documents plan states (Active, Superseded, Archived) and naming convention
- **Pre-quota checkpoint** — linked from README and HANDOFF; guarded procedure protects work when OpenCode usage limit approaches

## Repository map

| Path | What |
|---|---|
| `docs/prd/` | Requirements — authority for behavior; start at `docs/prd/INDEX.md` |
| `ADR/`, `docs/Decision Register.md` | Accepted decisions |
| `docs/A0-PROGRESS.md` | Increment log: what was done, evidence, defects |
| `docs/A1-PROGRESS.md` | A1 increment log: plan, owner blockers, evidence |
| `HANDOFF.md` / `docs/HANDOFF.md` | Current-state handoff (kept byte-identical) |
| `AGENTS.md` | Working agreements for AI-assisted development |
| `src/Modules/` | Built: PlatformCore, Integrations, MasterData. Planned, not built: Inventory (A1), Procurement (A2), Inbound (A2), Sales (A3), Fulfillment (A4), Shipping (A4), Finance (B1), Reporting (B3) |
| `.github/workflows/ci.yml` | CI definition (NFR-061) |
| `scripts/` | Quota and checkpoint tooling (see `scripts/README.md`) |
| `.kilo/plans/` | Execution plans (not authoritative; see `.kilo/plans/README.md`) |

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
# → { "userId", "accessToken", "refreshToken", "expiresAt" }
```

Refresh tokens rotate on use (PLT-011). Login/refresh are rate-limited per IP
and per account (SEC-04/PLT-012).

**Known gap (MD-004, slice A1):** role/branch assignment is not implemented
yet, so the first user has **no roles**. Login succeeds, but
permission-gated calls fail with 403 — including `POST /api/v1/users`, which
requires `platform.user.manage`. Roles land with slice A1 (MD-004).

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
dotnet ef migrations add <Name> \
  --project src/Modules/MasterData/SupplyForge.MasterData.Infrastructure \
  --startup-project src/Host/SupplyForge.Host   # migration **file only**; `database update` only with owner approval
```

CI applies all three to its own ephemeral database automatically.

### 6. Tests and format

```bash
dotnet test    # needs the compose SQL Server running (shared dev database for PlatformCore; throwaway databases for MasterData A1 integration tests)
dotnet format --verify-no-changes
```

`dotnet test` runs architecture tests (NFR-040), unit tests, and integration
tests against real SQL Server (NFR-050). PlatformCore integration tests connect
to the shared dev database; MasterData A1 integration tests create and drop
throwaway databases per test run. If you change the dev password, update the
test connection strings too.

### 7. Pre-quota checkpoint

See [`docs/pre-quota-checkpoint.md`](docs/pre-quota-checkpoint.md) for the
guarded checkpoint procedure (`scripts/quota-status.sh`, `scripts/checkpoint.sh`)
that protects work when the OpenCode usage limit approaches.

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
  -e "Auth__AccessTokenSecret=<32+ byte secret>" \
  supplyforge-host
```

Only build arguments are code; configuration (connection strings, JWT
settings, limits) is injected at run time, never baked into the image. The
image runs as **Production** by default, so `Auth__AccessTokenSecret` must be
a real ≥32-byte secret — startup fails fast on the repository placeholder
(SEC-20).

## CI (NFR-061)

Every PR runs `.github/workflows/ci.yml`: build → format check → migrations
against an ephemeral SQL Server service container → full test suite
(including architecture tests) → container build.

## Documentation pointers

- Requirements: [`docs/prd/INDEX.md`](docs/prd/INDEX.md) (navigation; part
  files win over the index)
- Decisions: [`ADR/`](ADR/), [`docs/Decision Register.md`](docs/Decision%20Register.md)
- Progress: [`docs/A0-PROGRESS.md`](docs/A0-PROGRESS.md), [`docs/A1-PROGRESS.md`](docs/A1-PROGRESS.md)
- Handoff: [`HANDOFF.md`](HANDOFF.md), [`docs/HANDOFF.md`](docs/HANDOFF.md) (byte-identical)
- Engineering agreements: [`AGENTS.md`](AGENTS.md)

## Latest verified build and test results

**Owner-reported (2026-11-24):**
- `dotnet build` — 0 errors / 0 warnings
- `dotnet test` — **249/249** (17 architecture + 68 PlatformCore unit + 24 MasterData unit + 92 PlatformCore integration + 48 MasterData integration), two consecutive runs
- `dotnet format --verify-no-changes` — clean
- Migration `20261124181017_A1WarehousesLocations` — created, **NOT applied**