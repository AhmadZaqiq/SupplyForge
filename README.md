# SupplyForge

Modular-monolith backend for the full goods lifecycle: supplier → purchase → receipt → stock → sale → shipment → invoice → payment → report. One company, three seeded branches. Portfolio backend, not a certified ERP.

## Status

**Active development — Slice A1 complete through A1-7.**

- **A0 (Foundations):** Complete — all gates closed (docs/A0-PROGRESS.md)
- **A1-1 … A1-4 Part 1:** Committed — Master Data foundation, product catalogue, partners, warehouses + storage locations
- **A1-4 Part 2:** Branch scope — completed, uncommitted (awaiting owner commit)
- **A1-5:** Inventory ledger + balances + WAC core — completed, uncommitted (awaiting owner commit)
- **A1-6:** WAC core (scale-8, receipt/issue/transfer blends) — completed, uncommitted (awaiting owner commit)
- **A1-7:** Documents that post movements (transfers, adjustments, opening import) — **complete, verified, uncommitted**

Slice-by-slice progress with evidence: [`docs/A0-PROGRESS.md`](docs/A0-PROGRESS.md) and [`docs/A1-PROGRESS.md`](docs/A1-PROGRESS.md). Session continuity: [`HANDOFF.md`](HANDOFF.md).

## Technology Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Language | C# | .NET 10 |
| Web Framework | ASP.NET Core Web API | — |
| ORM | EF Core | 10 |
| Database | SQL Server | 2022 |
| Testing | xUnit | — (unit + integration + architecture) |
| Container | Docker Compose | SQL Server service |
| CI | GitHub Actions | build → format → migrations → tests → container |
| API Docs | OpenAPI | generated from code |

## Architecture

SupplyForge is a **modular monolith**: one deployable Host process, with modules that own their tables and schemas. Each module follows Clean Architecture boundaries (Domain → Application ← Infrastructure), with endpoints and DI composition only in the Host.

```
API (Host) → Application (module) ← Domain (module) ← Infrastructure (module)
```

### Module Boundaries and Ownership

| Module | Schema | Owns |
|--------|--------|------|
| Platform Core | `platform` | users, roles, permissions, branch/warehouse scope, audit, notifications, API keys, auth |
| Integrations | `integrations` | outbox / integration-adjacent tables |
| Master Data | `master_data` | company, branches, product catalogue, partners, tax rates, payment terms, reason codes, warehouses, storage locations |
| Inventory | `inventory` | stock movements, stock balances, product cost (WAC), transfers, adjustments, opening imports |
| Shared kernel | cross-cutting | auditing interfaces, idempotency, sequencing, notifications contracts |

### Key Architectural Rules (enforced by architecture tests)

- **No module reads or writes another module's tables.** Cross-module reads go through public query contracts; writes go through public commands or events published to the outbox in the same transaction (MOD-1…MOD-10).
- **Only Inventory posts stock movements.** Only Finance owns amounts owed/paid and credit exposure. Sales never reads Finance tables (MOD-9).
- **Reporting is event-only and owns no operational writes** (MOD-6).
- **EF entities are never exposed through the API.** Request/response DTOs only.
- **Dependencies point inward only.** No circular references.

### Database Schema Organization

Each module owns its schema. Cross-module references are plain `Guid` columns (DM-4) with no EF foreign-key constraints across module schemas. Application-level validation enforces referential integrity.

## Implemented Functionality (through A1-7)

### Master Data and Organizational Structure

- **Company** — singleton legal entity with profile fields (base currency, timezone, default language)
- **Branches** — three seeded branches with unique codes
- **Product catalogue** — products with code, name, description, status lifecycle (Active/Inactive/Archived), category hierarchy, unit of measure with conversions
- **Partners** — suppliers and customers with statuses, credit limits, addresses (default shipping/billing)
- **Shared lookups** — tax rates, payment terms, reason codes
- **Warehouses** — branch-owned, unique company-wide code
- **Storage Locations** — seven location types (Receiving, Storage, Picking, Shipping, Returns, Quarantine, Blocked), warehouse-owned

### Branch and Warehouse Scope

- `ICurrentBranchScope` resolves per-request branch access (branch IDs + company-wide flag + `CanAccess(Guid)`)
- Fail-closed by default: a request with no accessible branch sees nothing
- System scope (for seed/migration/background jobs) is set only by the Host, never from HTTP
- Branch validation is enforced at the Inventory layer via `IWarehouseBranchResolver` (Inventory references MasterData.Contracts only — no cross-module implementation dependency)

### Inventory: Stock Movements and Posting

- **StockMovement** — immutable append-only ledger (INSERT-only after creation; INV-010/012)
- **Qty sign convention:** Issue/TransferOut/AdjustmentOut = negative; Receipt/TransferIn/AdjustmentIn/OpeningBalance = positive
- **StockBalance** — unique on (WarehouseId, ProductId); concurrency protected with HOLDLOCK/UPDLOCK
- **ProductCost** — WAC cost bucket per (ProductId, WarehouseId), scale-8 rounding
- **StockMovementPoster** — atomic posting inside the ambient session: HOLDLOCK on StockBalance + ProductCost, WAC application, idempotency claim/complete, no `FailAsync` (rollback via ambient transaction)
- **Negative stock protection** — poster rejects issues/transfers/adjustments that would drive on-hand below zero (INV-NEG)

### Warehouse Transfers and Partial Receiving

- **Transfer document lifecycle:** Draft → Approved → (same-warehouse: Closed atomically) / (inter-warehouse: In Transit → Received → Closed)
- **Same-warehouse location move:** TransferOut + TransferIn posted atomically in one ambient transaction → Closed
- **Inter-warehouse:** Shipping (TransferOut → In Transit) and Receiving (TransferIn → ReceivedQty increment) are separate ambient transactions per leg/receipt
- **Source WAC preservation:** TransferOut mutates `movement.UnitCost` in place; each partial TransferIn reads the original TransferOut's UnitCost — every partial receipt uses the same source WAC
- **Concurrent receive serialization:** `WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` on the Transfer row serializes concurrent receives; ShippedQty/ReceivedQty re-read inside the lock
- **Over-receipt prevention:** ReceiveQty cannot exceed remaining shipped quantity
- **Transfer shortfall reconciliation (INV-032):** transfer-shortfall Adjustment with `SourceDocumentType="Transfer"`, `SourceDocumentId=TransferId` — audit/documentation only, creates no StockMovements, Status=Posted; duplicate prevention via idempotency claim

### Adjustments (INV-040)

- **Adjustment lifecycle:** Draft → PendingApproval → Approved → Posted → Rejected
- **Normal adjustment:** posts AdjustmentIn/AdjustmentOut movements via the poster; negative-stock guard enforced
- **Transfer-shortfall adjustment:** audit-only, Status=Posted, no movements
- **Reject** permitted from Draft, PendingApproval, or Approved; not from Posted or Rejected
- **INV-041** (threshold approval, creator cannot approve) is blocked on B1 and not implemented

### Opening Imports (INV-014)

- **OpeningImport lifecycle:** Draft → Validated (dry-run, no movements) → Posted → Rejected
- **Dry-run validation:** validates qty > 0, LocationId present, warehouse exists and is branch-accessible; no StockMovements posted
- **Posting:** creates OpeningBalance movements via the poster; idempotency key per line (`opening:{ImportId}:line:{Index}`)
- **LocationId required** because the poster locks StockBalance by (ProductId, WarehouseId, LocationId)

### Weighted Average Cost (WAC)

- Bucket = ProductId + WarehouseId
- Opening blend, receipt blend, issue blend, transfer blend
- Scale-8 rounding, no negative average
- WAC-01…06, WAC-08…11 implemented; WAC-07 (reversal eligibility) blocked on OD-03

### Transaction Handling

- **AmbientDbSession** — one shared `SqlConnection` + `DbTransaction`, enlists every joined DbContext via `SetDbConnection` + `UseTransactionAsync`
- All joined contexts share the same connection string (one SQL Server, per-module schemas)
- Commit once; rollback on failure
- `InventoryDbContext.SaveChangesAsync` rejects Modified/Deleted on `StockMovement` (INSERT-only)

### Idempotency

- `IIdempotencyStore.TryClaimAsync` returns Claimed/AlreadyCompleted/PayloadMismatch/InProgress
- `CompleteAsync` marks completed; failed claims roll back with ambient transaction (no `FailAsync`)
- Idempotency keys include document type, document ID, and line index where applicable

### Concurrency Protection

- `StockBalance` locked with `HOLDLOCK` on (ProductId, WarehouseId, LocationId)
- `ProductCost` locked with `HOLDLOCK/UPDLOCK`
- Transfer row locked with `WITH (UPDLOCK, ROWLOCK, HOLDLOCK)` during receive
- Concurrent balance inserts serialized; retry allows all to succeed

### Auditability

- `RowVersion` (timestamp) concurrency tokens on StockBalance and document entities
- CreatedAt/UpdatedAt on all document entities via `TimeProvider`
- Audit log entries for platform operations

## Project Structure

```
SupplyForge/
├── src/
│   ├── Host/SupplyForge.Host/          # HTTP endpoints, auth wiring, DI composition, middleware
│   ├── Modules/
│   │   ├── PlatformCore/               # users, roles, permissions, scope, audit, notifications
│   │   ├── Integrations/               # outbox / integration tables
│   │   ├── MasterData/                 # company, branches, products, partners, warehouses, locations
│   │   └── Inventory/                  # stock movements, balances, WAC, transfers, adjustments, opening imports
│   └── Shared/                         # cross-cutting contracts (auditing, idempotency, sequencing)
├── tests/
│   ├── ArchitectureTests/              # dependency direction, module boundary rules
│   ├── PlatformCore.UnitTests/
│   ├── PlatformCore.IntegrationTests/
│   ├── MasterData.UnitTests/
│   ├── MasterData.IntegrationTests/
│   ├── Inventory.UnitTests/
│   └── Inventory.IntegrationTests/     # throwaway SQL Server databases per test run
├── docs/
│   ├── prd/                            # product requirements (INDEX.md → navigation)
│   ├── A0-PROGRESS.md                  # increment log (slice A0)
│   ├── A1-PROGRESS.md                  # increment log (slice A1)
│   ├── HANDOFF.md                      # session continuity
│   ├── Decision Register.md            # accepted owner decisions
│   └── DATABASE-GUIDE.md               # verified database structure
├── ADR/                                # accepted architecture decisions (001–006, 008, 009)
├── .kilo/plans/                        # execution plans (not authoritative)
├── scripts/                            # quota and checkpoint tooling
├── README.md                           # this file
├── AGENTS.md                           # AI-assisted development agreements
├── HANDOFF.md                          # root handoff (byte-identical to docs/HANDOFF.md)
└── docker-compose.yml                  # SQL Server service
```

## Business Workflows

### Inventory Movements

Stock movements are append-only. The `StockMovementPoster` posts movements inside an ambient transaction: it acquires `HOLDLOCK` on `StockBalance` and `ProductCost`, applies WAC logic, and saves — all inside the ambient transaction. Movements are immutable after creation (INV-010/012).

### Transfer Lifecycle

1. **Create** — Draft transfer between two locations (same or different warehouse). Branch isolation validated: both warehouses must be in an accessible branch. Inter-branch transfers are rejected until B1 (BRN-021).
2. **Approve** — Draft → Approved. No movements posted.
3. **Ship** — Approved → In Transit (inter-warehouse) or Closed (same-warehouse, both legs posted atomically). TransferOut posts at source WAC; the poster mutates `movement.UnitCost` in place.
4. **Receive** — In Transit → Received → Closed. Each partial receive posts TransferIn at the original TransferOut's UnitCost and increments `ReceivedQty`. Concurrent receives are serialized by Transfer-row HOLDLOCK. Over-receipt is rejected.
5. **Close** — requires `ReceivedQty >= ShippedQty` or a posted transfer-shortfall adjustment (INV-032). Cancelled or already-Closed transfers cannot be closed.
6. **Cancel** — Draft or Approved → Cancelled. No movements posted.

### Partial Receiving

Multiple partial receives are supported for inter-warehouse transfers. Each receive:
1. Locks the Transfer row (HOLDLOCK)
2. Checks idempotency pre-check (if a TransferIn with this SourceDocumentId exists, skip posting and skip ReceivedQty increment)
3. Validates remaining quantity
4. Posts TransferIn at source WAC
5. Increments ReceivedQty, advances status to Received/Closed

### Transfer Shortfall Reconciliation (INV-032)

When a transfer is partially received and cannot be fully completed, a transfer-shortfall Adjustment is created:
- `SourceDocumentType = "Transfer"`, `SourceDocumentId = TransferId`
- Status = Posted (audit-only)
- Creates **no** StockMovements
- Duplicate prevention: a second shortfall adjustment for the same TransferId is rejected
- Once posted, the transfer can be closed even with unreconciled shortfall

### Adjustments

Normal adjustments (INV-040):
1. Create Draft with lines (qty + movement type + product + warehouse + location)
2. Submit for approval (Draft → PendingApproval)
3. Approve (Draft/PendingApproval → Approved)
4. Post (Approved → Posted): posts AdjustmentIn/AdjustmentOut movements via the poster; negative-stock guard enforced
5. Reject (any pre-posted state → Rejected)

### Opening Imports (INV-014)

1. Create Draft with lines (product, warehouse, location, qty, unit cost)
2. Validate (Draft → Validated): dry-run, no movements; validates qty > 0, LocationId, warehouse existence, branch access
3. Post (Validated → Posted): creates OpeningBalance movements via the poster; idempotent per line
4. Reject (any pre-posted state → Rejected)

### Branch Isolation

- Every inventory table carries `WarehouseId` directly
- Inventory has no Warehouse entity; branch resolution is transitive via `IWarehouseBranchResolver`
- `StockMovementPoster` validates branch access before posting (fail-closed per BRN-011…014)
- Cross-module references are plain `Guid` columns, no EF FK across module schemas

### Negative Stock Protection

- The poster rejects any movement that would drive `StockBalance.QtyOnHand` below zero
- Applies to Issue, TransferOut, AdjustmentOut
- Verified by `INV_NEG_NegativeOnHand_IsRejected_ByPoster`

### Idempotency and Transaction Boundaries

- Every document post uses an idempotency key (document type + document ID + optional line index)
- `TryClaimAsync` joins the shared `SqlTransaction`; on ambient transaction rollback, the claim, movement, and balance all disappear (verified by `IDEM_06`)
- `CompleteAsync` marks the claim completed
- No `FailAsync` — failures roll back via the ambient transaction

## Development Setup

### Prerequisites

- .NET 10 SDK
- Docker with Compose (for SQL Server)

### 1. Clone the Repository

```bash
git clone https://github.com/AhmadZaqiq/SupplyForge.git
cd SupplyForge
```

### 2. Configuration

```bash
cp .env.example .env   # first time; adjust MSSQL_SA_PASSWORD if needed
```

The `.env` file is gitignored and contains only local development secrets. Never commit it.

### 3. Start SQL Server

```bash
docker compose up -d sqlserver
```

### 4. Run the Host

```bash
dotnet run --project src/Host/SupplyForge.Host
```

The host seeds the permission catalogue and system role bundles at startup (idempotent; no migration involved). The Master Data seed skips with a warning when `master_data` schema is absent, so a pre-A1 database still boots.

### 5. Migrations (owner approval required)

Migrations change module-owned schema and require owner approval before running. Migration files exist for:
- `20261124181017_A1WarehousesLocations` (Master Data: Warehouse + StorageLocation)
- `20261125171326_A1InventoryLedger` (Inventory: StockMovement + StockBalance + ProductCost)
- `20261126004151_A1TransferOrchestration` (Inventory: Transfer + Adjustment + OpeningImport)

All are **file only** — `dotnet ef database update` is never run on the dev database. CI applies migrations to its own ephemeral database.

When approved:
```bash
dotnet ef database update --project <module> --startup-project src/Host/SupplyForge.Host
```

### 6. Tests and Format

```bash
dotnet build                          # 0 errors, 0 warnings
dotnet test                           # unit + integration + architecture tests
dotnet format --verify-no-changes     # formatting check
```

Integration tests for Inventory create and drop throwaway SQL Server databases per test run. PlatformCore integration tests connect to the shared dev database.

## Testing and Verification

### Test Strategy

| Test Type | Project | Database | Requirement IDs in names |
|-----------|---------|----------|-------------------------|
| Unit | `tests/Inventory.UnitTests/` | None (pure domain logic) | Yes (NFR-051) |
| Integration | `tests/Inventory.IntegrationTests/` | Throwaway SQL Server per run | Yes (NFR-051) |
| Architecture | `tests/ArchitectureTests/` | None | Dependency direction rules |

### Verified Results (A1-7, 2026-11-26)

| Check | Result |
|-------|--------|
| `dotnet build` | 0 errors, 0 warnings |
| Inventory UnitTests | 45/45 passed |
| Inventory IntegrationTests (new A1-7 classes) | 32/32 passed |
| Inventory IntegrationTests (existing classes) | 40/40 passed |
| ArchitectureTests | 20/20 passed |
| `dotnet format --verify-no-changes` | Clean |
| Migrations applied to dev DB | None (file only) |

> Note: Running all integration test projects in parallel can cause SQL Server `CREATE DATABASE` contention (documented D0 finding in HANDOFF.md §12). Run classes individually or sequentially for reliable results.

## Project Status

### Implemented (through A1-7)

All functionality listed in the "Implemented Functionality" section above is complete and verified.

### Incomplete / Blocked

| Item | Blocker | Status |
|------|---------|--------|
| INV-041 (adjustment threshold approval) | B1 — approval engine (ADR-007 not drafted) | Blocked |
| SEC-40 (logo upload) | B2 — ADR-011 not drafted | Blocked |
| WAC-07 (reversal eligibility) | B3 — OD-03 open | Blocked |
| PLT-013 (password reset migration) | Owner migration approval required | Blocked |
| MD-003 (Employee directory) | Q7 — placement awaiting owner decision | Not scheduled |
| HTTP endpoints for Inventory | PLT-022 — no permission matrix exists | Not implemented (domain + infrastructure + tests only) |
| Branch scope (A1-4 Part 2) | Completed, uncommitted | Awaiting owner commit |
| Inventory ledger (A1-5) | Completed, uncommitted | Awaiting owner commit |
| WAC core (A1-6) | Completed, uncommitted | Awaiting owner commit |
| Transfer orchestration (A1-7) | Completed, verified, uncommitted | Awaiting owner commit |

### Future Roadmap (not implemented)

- A1-8: Availability + replenishment + returns guard
- A1-9: Admin user lifecycle (role/branch assignment, last-admin protection)
- A2: Buy & Receive (procurement, goods receipt)
- A3: Sell & Reserve (sales quotes, orders, reservations)
- A4: Fulfil & Dispatch (pick/pack, shipping)
- B1+: Finance (invoices, payments, AP/AR) and Reporting

## Documentation

| Document | Path | Purpose |
|----------|------|---------|
| Requirements | `docs/prd/INDEX.md` | Navigation to all PRD sections |
| Decisions | `ADR/`, `docs/Decision Register.md` | Accepted architecture decisions and owner decisions |
| Progress (A0) | `docs/A0-PROGRESS.md` | Slice A0 increment log with evidence |
| Progress (A1) | `docs/A1-PROGRESS.md` | Slice A1 plan, blockers, evidence |
| Handoff | `HANDOFF.md`, `docs/HANDOFF.md` | Session continuity (byte-identical) |
| Database | `docs/DATABASE-GUIDE.md` | Verified schema structure, ER diagrams |
| AI Agreements | `AGENTS.md` | Working agreements for AI-assisted development |
| CI | `.github/workflows/ci.yml` | Build → format → migrations → tests → container |

## Security Considerations

- JWT Bearer authentication (access tokens, 15-min TTL, HS256)
- API keys via `X-Api-Key` header for integration clients
- Authorization is **deny by default**: every request re-authorizes
- Endpoints declare named `perm:*` policies
- Unauthenticated/under-permitted callers get 401/403
- SEC-20: Production startup fails fast on placeholder secrets
- Rate limiting on auth endpoints (per IP and per account)
- No secrets, tokens, or passwords are hardcoded or logged
- `.env` is gitignored; real secrets arrive via environment variables

## Contribution Guidelines

1. Read `docs/HANDOFF.md` and `AGENTS.md` before starting work
2. Follow the reading protocol: PRD INDEX → Core set → slice files → one requirement at a time
3. Put requirement IDs in test names (NFR-051) and commit/PR titles
4. Run `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` after changes
5. Never commit, push, or deploy without explicit owner approval
6. Do not invent APIs, files, requirements, or results — ask when uncertain
7. Preserve existing uncommitted work; check `git status` before editing

## License

No license file exists in this repository. This is a portfolio project, not a certified ERP system.

## Disclaimer

SupplyForge is a portfolio backend demonstrating modular-monolith design patterns. It is **not** a certified ERP system and is not intended for production use without thorough security review, testing, and compliance validation.