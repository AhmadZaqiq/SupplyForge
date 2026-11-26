# SupplyForge — Project Completion Audit

**Date:** 2026-11-26
**Auditor:** Independent read-only repository audit (OpenCode session)
**Repository state audited:** `main` @ `29d65ba` ("A1-7: Transfer orchestration + A1-4 Part 2 + A1-5 + A1-6 completion"), working tree clean except `opencode.json`
**Scope of audit:** documentation, source, tests, database state, CI, and delivery plan — verified against the repository, not against the project's own progress documents.

**Audit rules honoured:** no application code, test, configuration, migration or document was modified. The only file created is this report. No migration was applied, nothing was committed or pushed, no data was deleted.

---

## 1. Executive Summary

| Measure | Value |
|---|---|
| **Overall estimated project completion** | **≈ 21 %** |
| **Estimated remaining work** | **≈ 79 %** |
| **Estimate range (confidence: medium)** | **18 % – 26 %** |
| **Current development stage** | P1A — slice **A1 (Master + Inventory core)**, increment A1-7 of A1-9 closed; slice **A0 closed**; slices **A2–B4 not started** |
| **Overall project health** | **Good inside the delivered slice, early-stage overall** |

**Candid assessment.** SupplyForge is a well-built *foundation*, not yet a product. Everything that exists is real, tested and green: 384 tests pass, the build is 0/0, formatting is clean, CI is green on GitHub, and the architectural boundaries (module ownership, contract-only cross-module access, ambient transactions, idempotency, branch scoping) are enforced by architecture tests rather than by convention. That is above the standard most portfolio backends reach.

But the project is roughly one-fifth complete. The business loop the PRD is about — *buy → receive → store → sell → ship → invoice → pay → report* — has **no implementation at all** beyond inventory stock posting: procurement, inbound, sales, fulfillment, shipping, finance and reporting are empty directories containing only `.gitkeep`. The HTTP API has **10 endpoints** (health, login, refresh, create-user, 3 notification, 3 API-key); there is not a single endpoint for products, partners, warehouses, stock, transfers or adjustments, and no module permission matrix exists. Six of nine EF migrations exist only as files; the development database has no `master_data` and no `inventory` schema, so even the completed master-data and inventory work cannot run against the shared dev database.

Two findings deserve immediate attention because they undermine how "done" is measured:

1. **`SupplyForge.slnx` omits both Inventory test projects.** Root-level `dotnet build`, root-level `dotnet test`, the CI workflow and `scripts/checkpoint.sh --verify-only` therefore never compile or run `tests/Inventory.UnitTests` (45 tests) and `tests/Inventory.IntegrationTests` (72 tests) — the 117 tests that cover the newest and riskiest code (A1-5/A1-6/A1-7). CI is green without them (run `45854768407`, 2026-11-26).
2. **`docs/HANDOFF.md` §2 states the A1-4 Part 2 → A1-7 work is "NOT committed"**, but commit `29d65ba` contains all of it and is pushed to `origin/main`. The handoff record contradicts Git; per `AGENTS.md` conflict rule this is reported, not resolved.

Neither issue changes the completion estimate downward — the Inventory tests do pass when run explicitly (verified in this audit) — but both mean the project's own verification loop has a blind spot exactly where the newest work sits.

---

## 2. Project Overview

### 2.1 What SupplyForge is intended to be

A **modular-monolith backend** for the full goods lifecycle:
`supplier → purchase → receipt → stock → sale → shipment → invoice → payment → report`.
One company, three seeded branches, single base currency (USD, scale 2) in P1. Explicitly a **portfolio backend, not a certified ERP** (`README.md`, PRD A-10).

### 2.2 Architecture

- **Stack:** C# / .NET 10, ASP.NET Core Web API, EF Core 10, SQL Server 2022, xUnit (unit + real-SQL-Server integration + architecture), Docker Compose, GitHub Actions, OpenAPI (`docs/prd/03-constraints-assumptions-defaults.md` §3.1).
- **Structure:** one Host (`src/Host/SupplyForge.Host`) as the only composition root; modules under `src/Modules/<Module>/{, .Contracts, .Infrastructure}`; shared contracts in `src/Shared/SupplyForge.Shared` (layout per accepted `ADR/ADR-001.md`).
- **Module rules:** each module owns its schema/tables (MOD-1); cross-module reads go through public query contracts, writes through public commands or events published to the outbox in the same transaction (MOD-3, OUT-01); only Inventory posts stock movements; only Finance will own money; Sales must never read Finance tables (MOD-9); Reporting is event-only (MOD-6). Enforced by `tests/ArchitectureTests/DependencyDirectionTests.cs` and `Mod1aSharedInterfaceTests.cs`.
- **Transactions:** `src/Host/SupplyForge.Host/Data/AmbientDbSession.cs` enlists every module DbContext on one shared `SqlConnection` + `DbTransaction` (ADR-003, MOD-TXN-01/04/05).
- **Authorization:** deny-by-default fallback policy + `perm:*` policy provider + permission catalogue seeded at startup (A0-3), JWT Bearer + `X-Api-Key`.

### 2.3 Scope and delivery plan (PRD §11.2)

| Stage | Slices | Contents |
|---|---|---|
| **P1A** | A0 Foundations · A1 Master + Inventory core · A2 Buy & receive · A3 Sell & reserve · A4 Fulfil & dispatch · A5 money stub (**owner default: SKIP**, TD-6/PA-17) | the runnable warehouse loop |
| **P1B** | B1 Invoice generation · B2 AP/AR depth · B3 Thin insight · B4 Hardening | money depth, thin reporting, hardening |
| **Not P1** | AI (§5.12), advanced integrations, lots, stock counts, PDF invoices, supplier agreements, customer returns | never implemented unless the owner says so |

**Denominator used in this audit:** P1 only (A0–B4, A5 counted as a zero-weight row because the owner decided SKIP). P2/P3 requirements are excluded *explicitly and visibly*, not silently.

### 2.4 Size of the intended scope (evidence)

- PRD split into 34 part files under `docs/prd/`; **459 unique requirement IDs** across them.
- Requirement IDs in the functional module files (§5) with **P1A/P1B/P1 priority tags**: 194 P1A + 52 P1B + 20 untagged-P1 mentions — i.e. roughly 266–300 P1 requirement statements in §5 alone, plus BRN/SEC/NFR/INT/DM/MOD cross-cutting IDs.
- Per-module unique IDs: PLT 35 · MD 24 · INV 56 · PRC 31 · INB 35 · SAL 32 · FUL 22 · SHP 20 · FIN 64 · FUL-status 3 · RPT 12 · BRN 26 · SEC 30 · INT 19 · NFR 52.

---

## 3. Verified Completed Work

All items below were verified in this audit by reading the implementation and/or executing the tests — not by trusting `docs/A0-PROGRESS.md` or `docs/A1-PROGRESS.md`.

### 3.1 Verification executed during this audit (2026-11-26)

| Check | Command / method | Result |
|---|---|---|
| Build | `dotnet build` | **0 errors, 0 warnings** (28.5 s) |
| Full suite (solution) | `dotnet test` | **267/267 passed** — Architecture 20, PlatformCore.Unit 75, MasterData.Unit 24, MasterData.Integration 56, PlatformCore.Integration 92 |
| Inventory unit (excluded from solution) | `dotnet test tests/Inventory.UnitTests` | **45/45 passed** |
| Inventory integration (excluded from solution) | `dotnet test tests/Inventory.IntegrationTests` | **72/72 passed** (1 m 45 s, throwaway SQL Server databases) |
| **Total** | — | **384/384 passed, 0 failed, 0 skipped** |
| Format | `dotnet format --verify-no-changes` | **exit 0** |
| CI on GitHub | `gh run list` | run **`45854768407` success** for `29d65ba` (2026-11-26T16:01Z); last 5 runs green |
| Dev database | read-only `sqlcmd` against `SupplyForge` | schemas `platform` + `integrations` only; **no `master_data`, no `inventory`**; 14 leftover throwaway test databases present |
| Solution contents | read `SupplyForge.slnx` | **7 test projects on disk, 5 in the solution** (both Inventory projects missing) |

### 3.2 Slice A0 — Foundations (gate closed 2026-11-19)

| Increment | What it delivered | Evidence |
|---|---|---|
| A0-1 | JWT access tokens (HS256, 15-min TTL, iss/aud/lifetime validation) | `src/Host/SupplyForge.Host/Auth/JwtAccessTokenIssuer.cs`; HTTP matrix tests in `tests/PlatformCore.IntegrationTests/HttpAuthMatrixTests.cs` (`PLT_011_*`) |
| A0-2/A0-2b | Rate limiting on auth endpoints + defect fixes (bounded partitions, bounded body buffering) | `src/Host/SupplyForge.Host/RateLimiting/`; `RateLimitHttpTests`, `RateLimiterEvictionTests` (`SEC_04_*`, 20 tests) |
| A0-3 | Permission catalogue, role bundles, deny-default `perm:*` policies, create-user bootstrap with `sp_getapplock` | `PermissionCodes.cs` (4 codes), `PermissionPolicyProvider.cs`, `PlatformCatalogueSeed.cs`; `EndpointAuthorizationCoverageTests`, `AlwaysTruePolicyTests`, `FailClosedAuthorizationTests` |
| A0-4 | BRN-011/013/014 HTTP status discrimination, `SensitiveDataRedactor` (PLT-044), OpenAPI dual-auth schemes (INT-052) | `tests/ArchitectureTests` + `SensitiveDataRedactorTests`; tests `BRN_011/013/014_ApiKeyRevoke_*` |
| A0-5 | CI workflow (build → format → ef migrations on ephemeral container → test → container build), README/runbook | `.github/workflows/ci.yml`; CI green on GitHub (verified via `gh`) |
| A0-6 | `AmbientDbSession` — one connection/transaction across module contexts; session-then-email (MOD-TXN-02) | `src/Host/SupplyForge.Host/Data/AmbientDbSession.cs`; `MOD_TXN_01/04/05_*` tests |
| A0-7 | Notification delivery state (Pending/Sent/Failed), `NotificationRetryWorker` with backoff → dead-letter, audited mark-sent | `src/Host/SupplyForge.Host/Notifications/NotificationRetryWorker.cs`; `NotificationDeliveryStateTests`, `NotificationDeliveryHttpTests` (`PLT_050/051/052_*`) |
| A0-8 | 6 hardening items: SEC-20 startup secret guard, CAS sequence allocation (PLT-073b), PLT-011, OUT-01/02/04/06, MD-004 checks | `tests/ArchitectureTests/StartupSecretGuardTests.cs` (`SEC_20_*` present and passing) |
| A0-9 | Login-enumeration fix on the wrong-password path (SEC-05/PLT-012) | `AuthService.LoginAsync`; `SEC_05_PLT_012_WrongPasswordBody_IsIndistinguishableFromUnknownUser_InAllThreeLockStates` (present, passing) |

**A0 gate (PRD §11.2):** sign-in ✅ · branch isolation tests ✅ (BRN-012 foreign-branch HTTP leg deferred, see §4) · notification round-trip ✅ (5/5 ADR-008 legs) · API-key scope test ✅ (INT-050) · CI green ✅ (verified). **Gate satisfied.**

### 3.3 Slice A1 increments (verified individually)

| Increment | Claim | Independent verification |
|---|---|---|
| **A1-1** Master Data foundation | Company singleton + 3-branch seed, `master_data` schema, ambient session 2→N | `Domain/Company.cs`, `Branch.cs`; migration `20261119232720_A1InitialMasterData`; tests `MD_001_*`, `MD_002_*`, `MOD_TXN_01/04/05_*` all pass |
| **A1-2** Product catalogue | Product/Category/UoM/conversions, statuses, Restrict FKs, `Factor > 0` check | `Domain/Product.cs` (all MD-010 fields present: `RequiresInspection`, `ReorderPoint`, `LeadTimeDays`, `DefaultPrice`, `StandardCost`); migration `20261120110027_A1ProductCatalogue`; 9 integration + 6 unit tests pass |
| **A1-3** Partners + shared master | Supplier/Customer + statuses + addresses with default filtered indexes, tax/terms/reason codes, 3 CHECK constraints | `Domain/Supplier*.cs`, `Customer*.cs`, `TaxRate.cs`, `PaymentTerm.cs`, `ReasonCode.cs`; migration `20261124011532_A1PartnersSharedMaster`; 17 integration + 11 unit tests pass |
| **A1-4 P1** Warehouses + locations | `Warehouse`, `StorageLocation`, `LocationType` (5 types), port `ILocationDeactivationGuard` + placeholder | `Domain/Warehouse.cs`, `StorageLocation.cs`, `LocationType.cs` (exactly Receiving/Quarantine/Storage/Returns/Staging); migration `20261124181017_A1WarehousesLocations`; 12 integration + 7 unit tests pass |
| **A1-4 P2** Branch scope | Fail-closed `ICurrentBranchScope` via scoped `IBranchScopeHolder`, per-request `BranchScopeMiddleware`, no static mutable scope | `PlatformCore.Infrastructure/Security/BranchScopeHolder.cs`, `Host/Middleware/BranchScopeMiddleware.cs`; arch test `Q_E_NoStaticSettableBranchScope_InSrc`; `BRN_012_TwoConcurrentScopes_EachSeesOnlyItsOwnBranch` passes |
| **A1-5** Inventory ledger | Immutable `StockMovement`, `StockBalance`, `ProductCost`, `StockMovementPoster` (HOLDLOCK, negative-stock guard, idempotency claim/complete, branch validation) | `StockMovementPoster.cs` read in full; `InventoryDbContext` rejects Modified/Deleted on `StockMovement`; CHECK `CK_StockBalance_OnHand_NonNegative`; tests `INV_010_*`, `INV_011_*`, `INV_NEG_*`, `IDEM_01…06`, `BRN_012_*` pass — **but see §4: two planned sub-items not delivered** |
| **A1-6** WAC core | Bucket = ProductId+WarehouseId, opening/receipt/issue/transfer blends, scale 8, no negative average | `Domain/ProductCost.cs` + `ApplyWacLogic` in poster; 26 WAC tests in `WacCoreTests.cs` (`WAC_02/03/04/05/06/08/09/10_*`) all pass; `WAC_07` correctly absent (blocked on OD-03) |
| **A1-7** Documents that post movements | Transfer lifecycle, partial receiving with source-WAC preservation, adjustment lifecycle, opening import dry-run→post, transfer-shortfall reconciliation | `TransferOrchestrator.cs` (430 lines, UPDLOCK/ROWLOCK/HOLDLOCK on receive), `AdjustmentOrchestrator.cs`, `OpeningImportOrchestrator.cs`; 21 transfer + 12 adjustment/opening integration tests pass, incl. the four documented corrective fixes (`INV_030_Transfer_CloseCancelled/Closed/Draft_Rejected`, `INV_032_Transfer_Shortfall_*`, `INV_040_Adjustment_SubmitForApproval_DraftToPendingApproval`, `INV_014_OpeningImport_Validate_NonExistentWarehouse_Rejected`) |

### 3.4 Supporting work

- **Documentation:** `docs/DATABASE-GUIDE.md` (32 tables described, ER diagrams), `README.md` runbook, `docs/A0-PROGRESS.md` (1,222 lines of evidence), `docs/A1-PROGRESS.md` (629 lines), `docs/Decision Register.md`, 8 accepted ADRs (`ADR/ADR-001…006, 008, 009`).
- **Tooling:** `scripts/quota-status.sh`, `scripts/checkpoint.sh` + 19-case self-test, `.opencode/commands/{slice,req,quota,checkpoint}.md`.
- **Traceability:** 79 distinct requirement IDs appear directly in test names (plus `MOD_TXN_*`, `INV_NEG`, `IDEM_*`, `SEC_20`, `NFR_050`, etc.) — NFR-051 is genuinely practised, not just claimed.

---

## 4. Partially Completed Work

| # | Item | What exists | What is missing | Evidence |
|---|---|---|---|---|
| 1 | **A1-5 as scoped** | Ledger, balances, cost bucket, poster, 16+ tests | The increment row in `docs/A1-PROGRESS.md` §1 says "reconciliation job comparing balances to movement sums, **history queries**, opening-balance dry-run" and is marked **Done**; its own §10 "Not done" list admits the **reconciliation job (INV-011 scheduled job)** and history queries were deferred. Only a *test* compares balance to movement sums. | `docs/A1-PROGRESS.md` §1 vs §10; `grep -rn "Reconcil" src` → no job |
| 2 | **A1 acceptance gate (PRD §11.2)** | Reconciliation (test-level) ✅ partial · WAC-02/03/08 tests ✅ | **INV-004 tests** only partially (on-hand ≥ 0 covered by `INV_NEG_*` + DB CHECK; `available ≥ 0` and no-double-reserve need reservations → A3) · **INV-041 via approval engine** blocked (ADR-007) · **logo upload tests** blocked (ADR-011) · no reconciliation *job* | `docs/A1-PROGRESS.md` §2 |
| 3 | **MD-042 "no deactivate while holding stock"** | `ILocationDeactivationGuard` port exists | Still the **public placeholder that allows everything**; `docs/A1-PROGRESS.md` D2 says Inventory implements it "in A1-5" — A1-5 shipped without it | `src/Modules/MasterData/.../Location/DefaultLocationDeactivationGuard.cs` ("PLACEHOLDER … allows all") |
| 4 | **Q-C company-wide user scope (BRN-010)** | `ICurrentBranchScope.IsCompanyWide` exists | `platform.User.IsCompanyWide` column/migration was never created (Q-C says "in Part 2"; Part 2 deferred it). No HTTP user can ever be company-wide; `BranchScopeProvider` comment confirms the column is pending. | `PlatformCore.Infrastructure/Services/BranchScopeProvider.cs:29`; no migration in `Data/Migrations/` |
| 5 | **Audit (PLT-040/041/043/045)** | `AuditLog` table, `IAuditWriter` choke point, PLT-044 redaction, 4 write call sites in `AuthService` | No before/after changed-field map (creates write `detailsJson = null`), no audit **search API**, no branch-scoped audit query | `PlatformSharedServices.WriteAsync`; `AuthService.cs:136` (`detailsJson: null`); no audit endpoint in `src/Host/.../Endpoints/` |
| 6 | **Outbox / events (OUT-01…07, PLT-070/071)** | `Outbox.EnqueueAsync` refuses to write outside a transaction (OUT-01), `InboxMessage` dedupe table | **No dispatcher, no retry/backoff/dead-letter for outbox rows, no event publisher anywhere in `src`, no consumer.** The event-driven half of the architecture is unwritten; the only background worker is `NotificationRetryWorker` | `grep -rn "IOutbox" src` → registration + `Outbox.cs` only; `docs/HANDOFF.md` §12 item 3 |
| 7 | **Module HTTP endpoints + permission matrices** | 10 platform endpoints; `PLT-022` coverage arch test passes | **Zero** endpoints for Master Data or Inventory; only 4 permission codes exist. PRD `06b` §6.4.1 says *"Full permission matrices produced at module design time (not optional)"* — none produced yet | `grep -rn "MapGet|MapPost" src/Host` → 10 hits; `PermissionCodes.cs` (4 constants) |
| 8 | **Migrations vs databases** | 9 migration files exist (Platform 2, Integrations 1, MasterData 4, Inventory 2) | Only **3 applied** to the dev DB; `master_data` and `inventory` schemas do not exist there, so `MasterDataSeed` always logs "skipped". The 3 A1 migrations are "file only" pending owner approval | `sqlcmd` on `SupplyForge`; `Program.cs` seed skip warning |
| 9 | **CI vs Inventory tests** | CI green on GitHub | CI runs `dotnet build` + `dotnet test` at root → **Inventory test projects are never built or run in CI** (`SupplyForge.slnx` omits them; `git log -S "Inventory.IntegrationTests" -- SupplyForge.slnx` → empty) | `SupplyForge.slnx` lines 31–46 |
| 10 | **Documentation drift** | Docs are otherwise unusually detailed | (a) `docs/HANDOFF.md` §2 says the A1-4P2→A1-7 work is **NOT committed** — it is committed as `29d65ba` and pushed. (b) `README.md:71` claims **"seven location types (Receiving, Storage, Picking, Shipping, Returns, Quarantine, Blocked)"** — the code has **five** types (D-16) and `Blocked` is a flag; Picking/Shipping are not location types (PRD A-7). (c) `README.md:84` says StockBalance is unique on `(WarehouseId, ProductId)` — the key includes `LocationId`. (d) `docs/DATABASE-GUIDE.md:769` says "Inventory … has no tables yet" — 8 tables now exist in migrations. (e) the recorded A1-5 rule "No `var` in C# source" is violated 189 times in `src` | see §10 |
| 11 | **WAC-11 (Inventory ADR)** | WAC behaviour implemented + tested | WAC-11 requires *"the Inventory ADR shall define WAC cost-pool membership, transfer treatment, reversal eligibility and concurrency"* — no such ADR exists (`ADR/` has 001–006, 008, 009) | `ADR/` listing |
| 12 | **Document numbering (PLT-073)** | `DocumentSequence` table + CAS-based `NextAsync` (A0-8) | No business document consumes it yet — transfers/adjustments/opening imports carry no document numbers | `grep "IDocumentSequenceService"` in `src/Modules/Inventory` → 0 hits |

---

## 5. Not Started Work

### 5.1 Empty modules (verified: only `.gitkeep` in each directory)

`src/Modules/Procurement` · `src/Modules/Inbound` · `src/Modules/Sales` · `src/Modules/Fulfillment` · `src/Modules/Shipping` · `src/Modules/Finance` · `src/Modules/Reporting` — **7 of 11 module directories contain no code.**

### 5.2 By slice

| Slice / area | Requirement IDs (approx.) | Status |
|---|---|---|
| **A1-8** Availability + replenishment + returns guard | INV-100, INV-060, INV-061, INV-090, INV-091, §6.2 | **Not started** (planned next per `docs/A1-PROGRESS.md` §1) |
| **A1-9** Admin user lifecycle | MD-004, BRN-009, BRN-010, PLT-ROLE-3 | **Not started** |
| **MD-003** Employee directory | MD-003 | **Not scheduled** — placement proposal awaits owner (Q7) |
| **Inventory remainder** | INV-002/003/004 (buckets + invariants), INV-013 history, INV-063/064/065 incoming projection, INV-081 query, INV-041 (blocked), WAC-07 (blocked), WAC-11 | **Not started / blocked** |
| **A2 Buy & receive** | PRC (31 IDs, ~21 P1), INB (35 IDs, ~30 P1), INV-063/064, ADR-015, ADR-016 | **Not started** |
| **A3 Sell & reserve** | SAL (32 IDs, ~19 P1), INV-020…026 reservations, availability, FIN-041 credit contract, FIN-CALC-01…12 pricing, MOD-9 arch test, OD-04/OD-05 | **Not started** |
| **A4 Fulfil & dispatch** | FUL (22 IDs, ~18 P1), SHP (20 IDs, ~12 P1), dispatch stock (D-4), TD-10 shipment TXN2 | **Not started** |
| **A5 Money stub** | manual invoice | **SKIP by owner default** (TD-6 / PA-17) — re-add only if a pre-B demo needs it |
| **B1 Invoice generation** | FIN-027…039, FIN-029a, FUL-036…038, ADR-013, ADR-014 | **Not started** (2 ADRs not drafted) |
| **B2 AP/AR depth** | supplier invoice + 3-way match, payments, allocations, credit notes, exposure components, cross-branch payments (TD-8), FIN-CALC-13…16, PRC-055 counters | **Not started** |
| **B3 Thin insight** | RPT-001…004/010/020 thin, NFR-025 W5 | **Not started** |
| **B4 Hardening** | NFR-020…023 perf targets, W1–W5 workloads, NFR-052 demo/load data generator (P1A), security pass, one-command run (NFR-060 compose `app` service), final write-up | **~25 %** (CI, README, docs, A0-8/A0-9 hardening exist; perf, demo data, security pass, compose app service missing) |
| **Platform remainder** | PLT-013 (blocked), PLT-023/024 record-level + nested-resource rules, PLT-030…034 approval engine (blocked), PLT-041/045 audit query, PLT-071 background jobs, PLT-076 pagination, SEC-40 (blocked), password policy, refresh-token concurrency token | **Not started / blocked** |

---

## 6. Weighted Completion Breakdown

### 6.1 Method

Weights are derived from the project's own delivery plan (`docs/prd/11-delivery-plan.md` §11.2) cross-checked against requirement density per module file (§5 ID counts, §11.3 P1 gate mapping) and against the relative engineering complexity of each slice (domain rules + concurrency + integration vs. CRUD). Cross-cutting API surface is broken out as its own area because it spans every slice and is currently near-zero. Completion within an area is estimated from *verified implementation + tests*, not from progress documents. P2/P3 are excluded from the denominator (PRD §0 rule 2); A5 is shown with weight 0 because the owner explicitly decided SKIP (PA-17/TD-6) — it is not silently dropped.

### 6.2 Table

| Project Area | Weight | Area Completion | Weighted Contribution | Remaining Scope | Confidence |
|---|---:|---:|---:|---|---|
| Platform foundations (A0: auth, RBAC, branch scope, audit, notifications, outbox/idempotency/sequences, CI, arch tests) | 13 | 70 % | 9.10 | Audit search + before/after capture; password policy; refresh-token concurrency token; outbox dispatcher + OUT-02/03; PLT-013; PLT-071 jobs; PLT-073 consumers; Docker/`sa` hardening | Medium |
| Master Data (A1: company, branches, products, partners, lookups, warehouses, locations) | 7 | 60 % | 4.20 | MD-003, MD-004 (A1-9), MD-042 deactivation guard enforcement, query/CRUD services, demo seed rows | Medium |
| Inventory core (A1-5/6/7 + A1-8: ledger, WAC, transfer/adjustment/opening documents, availability, replenishment, returns guard) | 14 | 45 % | 6.30 | A1-8 (INV-100/060/061/090/091), reconciliation job (INV-011), history queries (INV-013), incoming projection (INV-063/064), INV-041, WAC-07, WAC-11 ADR | Medium |
| HTTP API layer + module permission matrices (endpoints, DTOs, validation, pagination, IDOR/404-403 at HTTP, OpenAPI) | 9 | 12 % | 1.08 | Every business endpoint; module permission matrices per `06b` §6.4.1; audit query API; list/pagination (PLT-076); HTTP-level branch isolation for business resources | Medium |
| Procurement + Inbound (A2) | 14 | 0 % | 0.00 | Everything: requisitions, RFQ/compare/award, PO + revisions, GR, inspection, putaway, supplier returns, incoming projection | High |
| Sales + reservations + credit (A3) | 13 | 0 % | 0.00 | Everything: SO, pricing, availability/reservations, backorder, credit reservation contract, MOD-9 arch test | High |
| Fulfillment + Shipping (A4) | 10 | 0 % | 0.00 | Everything: pick, pack, dispatch (D-4), shipment row, tracking, delivery confirmation | High |
| Finance — invoice generation (B1) | 6 | 0 % | 0.00 | Everything: per-dispatch `InvoiceGeneration`, statuses incl. Skipped, retry, reconcile, FUL-036…038 | High |
| Finance — AP/AR depth (B2) | 8 | 0 % | 0.00 | Everything: supplier invoice + 3-way match, payments, allocations, credit notes, exposure, cross-branch rules | High |
| Reporting — thin insight (B3) | 3 | 0 % | 0.00 | Everything: dashboard, stock/open-doc reports, gen-failure widget | High |
| Hardening & NFR (B4) | 3 | 25 % | 0.75 | Perf W1–W5 (NFR-020…022, NFR-025), NFR-052 demo data generator, security pass, one-command run, final write-up | Medium |
| A5 money stub | 0 | n/a (owner decision: SKIP) | 0.00 | Re-add only if a pre-B demo requires it (TD-6/PA-17) | High |
| **Total** | **100** | — | **21.43** | **≈ 79 % of weighted scope remains** | — |

### 6.3 Calculation

```
13×0.70 = 9.10   7×0.60 = 4.20   14×0.45 = 6.30   9×0.12 = 1.08
3×0.25  = 0.75   all other areas = 0.00
---------------------------------------------------------------
Overall = 9.10 + 4.20 + 6.30 + 1.08 + 0.75 = 21.43 ≈ 21 %
Remaining = 100 − 21 = 79 %   (range 74–82 %; estimate range 18–26 %)
```

### 6.4 Three different completion numbers (do not confuse them)

| Dimension | Estimate | Basis |
|---|---|---|
| **Implementation completion** | **≈ 21 %** | weighted table above |
| **Verification of what is implemented** | **≈ 85 %** | 384/384 tests green; requirement-ID traceability; real-SQL-Server integration tests; architecture tests. Deductions: no HTTP/E2E tests for business behaviour (no endpoints exist), outbox path untested end-to-end, no performance tests |
| **Verification completion of the whole project** | **≈ 19 %** | 79 requirement IDs with named tests out of ~266–300 P1 statements in §5 (~20–27 %), consistent with the weighted figure |
| **Overall project completion** | **≈ 21 % (18–26 %)** | implementation is the binding constraint, not test volume |

**Uncertainty drivers:** (a) weights are judgement, ±3 pp; (b) how much of the API layer each module needs is unknown until the first module endpoint slice runs; (c) A1-8/A1-9 size is estimated from requirement counts only. The estimate is *not* sensitive to the Inventory-test blind spot — those tests pass when run.

---

## 7. Completed Milestone Verification

| Milestone | Classification | Evidence / reasoning |
|---|---|---|
| **A0-1 JWT** | **Verified Complete** | Tests present and passing (`PLT_011_*`); HTTP matrix boots the real Host |
| **A0-2/02b Rate limiting + fixes** | **Verified Complete** | 20 `SEC_04_*` tests pass; eviction/concurrency edge cases covered |
| **A0-3 Permissions / deny-default** | **Verified Complete** | `EndpointAuthorizationCoverageTests`, `AlwaysTruePolicyTests` (behavioral scan), `FailClosedAuthorizationTests` all pass |
| **A0-4 BRN matrix, redaction, OpenAPI** | **Complete with Minor Issues** | BRN-011/013/014 + PLT-044 + INT-052 verified; **PLT-013 deferred by owner** (migration approval), **BRN-012 foreign-branch leg at HTTP deferred** — only service-level `BRN_012_*` tests exist today |
| **A0-5 CI + README** | **Complete with Minor Issues** | CI green (verified via `gh`), but **excludes the Inventory test projects**; README contains factual errors (§4 item 10); NFR-060 compose `app` service not implemented (recorded in A0-PROGRESS) |
| **A0-6 Ambient transaction spike** | **Verified Complete** | `MOD_TXN_01/04/05_*` pass; `Outbox.EnqueueAsync` fails fast without a transaction |
| **A0-7 Notification delivery state** | **Verified Complete** | `PLT_050/051/052_*` + `TD_2_*` tests pass; only migration applied to dev DB beyond the initial two |
| **A0-8 Hardening (6 items)** | **Verified Complete** | `SEC_20_*` arch tests pass against the real `appsettings.json`; commits `6085e9c`, `da012a6` … present in history |
| **A0-9 Login enumeration** | **Verified Complete** | `SEC_05_PLT_012_*` present and passing |
| **A0 gate (PRD §11.2)** | **Verified Complete** | All five gate items evidenced; deferrals documented in `docs/A0-PROGRESS.md` |
| **A1-1 Master Data foundation** | **Verified Complete** | Migration + seed + `MD_001/MD_002` tests pass |
| **A1-2 Product catalogue** | **Verified Complete** | All MD-010 fields present; `MD_010…013`, `DM_2` tests pass |
| **A1-3 Partners + shared master** | **Verified Complete** | `MD_020/030/031/050` tests pass incl. CHECK and filtered-index assertions |
| **A1-4 (Part 1 + Part 2)** | **Complete with Minor Issues** | Entities, migration, branch-scope mechanism and arch tests verified; **Q-C `IsCompanyWide` column never created**, **MD-042 guard still a placeholder**, 189 `var` usages contradict the recorded "no `var`" rule |
| **A1-5 Inventory ledger** | **Partially Complete** | Core ledger/poster/idempotency/branch tests verified green, **but the increment row claims "Done" while reconciliation job and history queries were deferred by its own §10** |
| **A1-6 WAC core** | **Complete with Minor Issues** | 26 WAC tests pass (WAC-02/03/04/05/06/08/09/10); WAC-07 legitimately blocked on OD-03; **WAC-11 Inventory ADR not drafted** |
| **A1-7 Documents that post movements** | **Verified Complete** | 72 integration tests pass, including all four documented corrective-fix regressions and concurrency cases |
| **A1 acceptance gate (PRD §11.2)** | **Partially Complete** | 3 of 5 gate items evidenced (reconciliation test-level only, WAC-02/03/08, INV-004 partial); **INV-041 and logo upload blocked**; no reconciliation job |
| **D0 `DATABASE-GUIDE.md`** | **Complete with Minor Issues** | Structure verified for platform/integrations/master_data, but now stale on Inventory ("has no tables yet") |
| **R0 repository tidy-up** | **Verified Complete** | Layout matches `ADR-001`; scripts and docs in place |
| **CI on GitHub** | **Complete with Minor Issues** | Last 5 runs green (verified), but the green signal omits 117 Inventory tests and applies no Inventory migration |

---

## 8. Remaining Roadmap

The ordering below **is** the project's own roadmap (`docs/prd/11-delivery-plan.md` §11.2 and `docs/A1-PROGRESS.md` §1). No new roadmap is invented; two discrepancies with the existing documents are flagged.

### Phase 1 — finish A1 (immediate)
1. **Owner review/commit reconciliation** — the A1-4P2→A1-7 work is already committed (`29d65ba`); the handoff record must be corrected (discrepancy 1).
2. **Fix the solution file** — add `tests/Inventory.UnitTests` and `tests/Inventory.IntegrationTests` to `SupplyForge.slnx` so build/test/CI/checkpoint cover them (discrepancy 2: this contradicts nothing in the PRD but contradicts `README.md`'s "dotnet test = unit + integration + architecture" claim).
3. **A1-8** — availability query with all buckets + incoming, reorder breach alert (INV-060/061), Returns-location inbound guard (INV-090/091), INV-100.
4. **A1-9** — admin user lifecycle (MD-004, BRN-009/010, PLT-ROLE-3); plus the pending Q7 decision on MD-003.
5. **Owner decisions needed for A1 gate items:** ADR-007 (→ INV-041 + PLT-030…034), ADR-011 (→ SEC-40 logo), OD-03 (→ WAC-07), Q-C migration approval, reconciliation-job placement.

### Phase 2 — A2 Buy & receive
PRC requisitions → RFQ/compare/award → PO + revisions → GR → partial inspection → putaway → supplier return; plus incoming projection (INV-063/064), approval engine consumption (PRC-003), and ADR-015/ADR-016. **Depends on:** approval engine (ADR-007) for requisition approval; OD-02/OD-07 for partial-close/GR-incoming behaviour.

### Phase 3 — A3 Sell & reserve
SO + pricing (FIN-CALC-01…12) + availability/reservations (INV-020…026) + credit-reservation contract (FIN-041, `ICreditExposureQuery`/`IReserveCredit`) + backorder/cancel + MOD-9 architecture test. **Depends on:** TD-3 credit-fails-closed decision, OD-04/OD-05, and A1-8 availability.

### Phase 4 — A4 Fulfil & dispatch
Pick, pack, **dispatch stock at D-4**, shipment row as second idempotent TXN (TD-10), manual tracking, delivery confirmation.

### Phase 5 — P1B money (B1 → B2)
B1 per-dispatch invoice generation (ADR-013, ADR-014 required first) → B2 supplier invoice + three-way match, payments/allocations/credit notes, cross-branch rules (TD-8).

### Phase 6 — B3 thin insight → B4 hardening
Branch-scoped reports + dashboard → perf W1–W5, NFR-052 demo data generator, security pass, one-command run, traceability write-up.

### Cross-cutting work that must land alongside (not a new phase)
Outbox dispatcher (OUT-02/03/07), module permission matrices (`06b` §6.4.1), audit search + before/after capture, document numbering consumers (PLT-073), pagination (PLT-076), password policy, refresh-token concurrency token, container/`sa` hardening, test-parallelism fix.

**Discrepancies with existing documents:** (1) `docs/A1-PROGRESS.md` §1 marks A1-5 "Done" although two of its five listed deliverables are deferred — the row should read *Done with deferrals*; (2) `docs/HANDOFF.md` §2 still describes committed work as uncommitted.

---

## 9. Blockers and Dependencies

| # | Blocker | Type | Effect on roadmap | Prevents |
|---|---|---|---|---|
| B-1 | **ADR-007 (approval engine) not drafted** | Missing ADR | Blocks A1 gate item INV-041/PLT-030…034 **and** A2 requisition approval (PRC-003) — A2 cannot complete without it | Development of approval flows; A1 gate closure; A2 acceptance |
| B-2 | **ADR-011 (logo scope) not drafted** | Missing ADR | Blocks SEC-40 gate item; also carries the SEC-50/INT-005 → P2 justification | A1 gate closure |
| B-3 | **ADR-013 (dispatch stays billable), ADR-014 (USD scale 2)** | Missing ADRs | Required **before B1** money code (Decision Register §1/§6) | B1 development |
| B-4 | **ADR-015 / ADR-016** | Missing ADRs | Required for A2 inbound cancel/revised behaviour and PRC-055 invoiced counters (activate at B2) | A2 completeness; B2 counters |
| B-5 | **OD-03 WAC reversal eligibility (WAC-07)** | Owner decision | Reversal-eligibility rules and reversal tests cannot be written; WAC-11 ADR also waits | Cost-reversal feature; A1 gate completeness |
| B-6 | **OD-02 (partial-close event), OD-07 (GR decrements incoming, ADR-020)** | Owner decisions | A2 PO-close/incoming semantics | A2 design |
| B-7 | **OD-04 / OD-05 (line-discount approval, credit override)** | Owner decisions | A3 pricing/approval behaviour | A3 design |
| B-8 | **Q-C migration approval (`platform.User.IsCompanyWide`)** | Migration approval | No HTTP user can be company-wide today; BRN-010 semantics incomplete (fail-closed only) | Company-wide roles at HTTP; A1-9 |
| B-9 | **PLT-013 password-reset migration approval** | Migration approval | A0 item stays deferred | Platform completeness |
| B-10 | **Migration application approvals** (3 A1 migration files, file-only) | Migration approval | Dev DB has no `master_data`/`inventory` schema → the Master Data seed always skips; no demo/E2E possible on the shared dev database; `DATABASE-GUIDE` examples cannot be run | Integration testing against dev DB; demo readiness |
| B-11 | **Q7 placement of MD-003; Q-3 branch code placeholders; Q-5 email-in-P1; Q-8 audit retention** | Open questions | MD-003 unscheduled; seed/demo values placeholder | Master Data completeness; demo |
| B-12 | **Module permission matrices not produced** (`06b` §6.4.1: "not optional") | Design artifact | No module endpoints can be added without inventing codes (PLT-022 forbids inventing) — the project currently treats this as a hard stop for all endpoint work | **All business API development** |
| B-13 | **Outbox has no owner for the dispatcher** (OUT-03/OUT-07, PLT-071) | Unowned component | No cross-module event can actually be delivered → the event half of MOD-3/E is unproven | A2+ event-driven flows; Reporting (event-only) |
| B-14 | **Recurring `Execution Timeout Expired` under parallel integration runs** (D0 finding, HANDOFF §12) | Infrastructure risk | Test-suite reliability; options A–E documented, none applied | Trustworthy CI/local sweeps at scale |

**Risk note:** B-12 and B-13 are the two blockers that gate the largest amount of future work; B-1/B-3 gate the money and approval paths.

---

## 10. Technical Debt and Risks

Prioritised by severity.

| Sev | Item | Detail | Recommended action (not performed) |
|---|---|---|---|
| **Critical** | Inventory test projects missing from `SupplyForge.slnx` | `dotnet build`, `dotnet test`, CI and `checkpoint.sh --verify-only` never touch `tests/Inventory.*` (117 tests). CI green ≠ Inventory verified. Root cause: `SupplyForge.slnx` was updated for Inventory *source* projects only (`git log -S` shows the test projects were never added) | Add the two projects to the solution; confirm CI then runs 384 tests |
| **High** | No business API surface | 10 endpoints total; no Master Data/Inventory endpoints; only 4 permission codes; no module permission matrix produced; no IDOR/404-403 HTTP tests for business resources (BRN-011…014 leg exists only for API-key deletion) | Produce permission matrices per `06b` §6.4.1, then an endpoint increment per module |
| **High** | Outbox is write-only | `OutboxMessage` rows are enqueued transactionally but never dispatched; no publisher, no consumer, no retry/dead-letter (OUT-02/03/07), no `BranchId`/`CausationId`/retry-scheduling columns (HANDOFF §12 item 3) | Assign an owner and an ADR-backed dispatcher increment before A2 events matter |
| **High** | Six of nine migrations unapplied; dev DB lacks two schemas | Master Data + Inventory work is unrunnable against the shared dev database; only throwaway test databases exercise it | Owner-approved `dotnet ef database update` for MasterData + Inventory |
| **High** | Security baseline gaps (documented, unfixed) | No password policy anywhere (length/complexity/expiry/history); PBKDF2 at 100,000 iterations; `RefreshToken` has **no concurrency token** → concurrent refreshes can both succeed; `Dockerfile` runs as **root** (no `USER`); app connects to SQL Server as **`sa`** with the repository placeholder password in `appsettings.json` (mitigated by SEC-20 outside Development); API-key auth is a manual header check inside one endpoint, not a scheme | Address before any "production-ready" claim; B4 security pass at latest |
| **Medium** | Test-suite parallelism/timeout | D0 measured 4 testhosts + 4 Hosts, SQL at 110 % CPU, RAM 4.2→1.6 GB; recurring `Execution Timeout Expired`; no `xunit.runner.json`/`.runsettings`; PlatformCore integration tests share the dev DB while others create/drop DBs on the same server | Apply option A (sequential integration projects) or B (cap xUnit parallelism) |
| **Medium** | 14 leftover throwaway test databases | `SupplyForge_a14p2*`, `_a1catmig*`, `_mdsingleton*` etc. still present on the SQL container — failed/crashed runs did not drop them | Owner-approved cleanup script with a `finally`-based drop |
| **Medium** | MD-042 guard is a placeholder | "No deactivate while holding stock" is unenforced (allows everything); the plan said Inventory implements it in A1-5 | Implement `ILocationDeactivationGuard` against `StockBalance` |
| **Medium** | Documentation drift | HANDOFF "NOT committed" vs `29d65ba`; README "seven location types" vs five; README StockBalance key missing `LocationId`; DATABASE-GUIDE "Inventory has no tables yet"; A1-PROGRESS A1-5 row overstates completion; recorded "no `var`" rule violated 189× in `src` | One documentation-sync pass (docs only) |
| **Medium** | No E2E / demo path | Because of B-10 there is no runnable end-to-end business flow on any shared database; the project cannot yet demonstrate its headline lifecycle | Unblocks with migrations + A2…A4 |
| **Low** | `.env`/placeholder secret hygiene | `.env` gitignored and untracked (verified); `.env.example` carries the documented dev password — acceptable for a portfolio project but must never be reused | Keep as is; document clearly |
| **Low** | No license file | `README.md` states none exists | Decide before publication |
| **Low** | `SF.png` missing | Referenced by PRD Appendix B/AGENTS.md | Owner to add |

---

## 11. Testing and Quality Assessment

### 11.1 Results actually executed in this audit

```
dotnet build                      → 0 errors, 0 warnings            ✅
dotnet test (root solution)       → 267/267 passed                  ✅
dotnet test tests/Inventory.UnitTests      →  45/45 passed          ✅
dotnet test tests/Inventory.IntegrationTests→  72/72 passed          ✅
dotnet format --verify-no-changes → exit 0                          ✅
gh run list (CI)                  → 45854768407 success (29d65ba)   ✅
```

**Total: 384/384 tests passing, 0 failing, 0 skipped.** Full output saved at `/tmp/audit-test-run-1.txt`, `/tmp/audit-inv-unit.txt`, `/tmp/audit-inv-int.txt`. (Per the project's test-failure rule, output was captured with `tee` before reading.)

### 11.2 Assessment of the testing strategy

**Strengths (verified, not assumed):**
- **Real-database integration tests** — Inventory and Master Data integration projects create/drop throwaway SQL Server databases and run real EF migrations (`tests/Inventory.IntegrationTests/ThrowawayDatabase.cs`, `MigrateAsync` per context). This is the right pattern and it works.
- **Requirement-ID traceability is real** — 79 distinct IDs appear in test names; examples such as `INV_030_Transfer_InterWarehouse_SourceWAC_Preserved` assert the actual business rule (source WAC preserved on partial receipts, destination blend), not just "no exception thrown".
- **Architecture tests enforce decisions** — dependency direction, contract-leaf rule, shared-schema separation, no static mutable branch scope, no always-true auth policy, startup secret guard.
- **Concurrency is tested** — `INV_NEG_ConcurrentNewBalance_AllSucceedViaRetry`, `INV_032_Transfer_Shortfall_Concurrent_DuplicatePrevented`, `BRN_012_TwoConcurrentScopes_EachSeesOnlyItsOwnBranch`, `IDEM_04_ConcurrentSameKey_*`.
- **Regression tests exist for every corrective fix** found during A1-7 (close-status validation, shortfall validation, PendingApproval reachability, opening-import warehouse validation).

**Weaknesses:**
1. **CI does not run the Inventory tests** (§10 Critical) — the single biggest verification gap.
2. **No HTTP/E2E tests for business behaviour** — unavoidable while no business endpoints exist, but it means BRN-011…014, IDOR, and permission coverage for business resources are unproven (the current HTTP matrix covers auth/notification/API-key routes only).
3. **The outbox/event path is untested end-to-end** — `OUT_01`/`OUT_02` tests exist at store level, but no publish→dispatch→consume cycle exists to test.
4. **No performance tests** — NFR-025 W1–W5 workloads are unmeasured (B4).
5. **Intermittent suite instability** — documented `Execution Timeout Expired` under parallel execution (D0); the full suite passed in this audit on the first attempt, so the issue is load-dependent, not deterministic.
6. **`INV_011` reconciliation is a test assertion, not a job** — the PRD requires a reconciliation job; a passing test is not the same deliverable.

**Verdict:** for the ~21 % that exists, verification quality is high (≈85 %). The risk is *coverage of what is not yet written* and *the CI blind spot*, not weak tests.

---

## 12. Final Assessment

| Question | Answer |
|---|---|
| **Overall completion** | **≈ 21 %** |
| **Remaining** | **≈ 79 %** (range 74–82 %) |
| **Estimate range / confidence** | **18 % – 26 % / medium** |
| **Implementation vs verification** | implementation ≈ 21 %; verification of implemented work ≈ 85 %; verification of the whole project ≈ 19 % |

### What has been achieved so far

- A complete, **tested platform foundation**: JWT auth with lockout/rate limiting, deny-default permission system with a behavioural always-true-policy scan, fail-closed branch scoping, audit with redaction, notifications with delivery state and retry/dead-letter, API keys, ambient cross-module transactions, transactional outbox writes, idempotency claims, CI that is green on GitHub, and architecture tests that enforce the module rules.
- **Master Data as a data model**: company singleton, 3-branch seed, product catalogue with categories/UoM/conversions, suppliers/customers with addresses and status lifecycles, tax rates/payment terms/reason codes, warehouses and five-type storage locations — all with migrations, CHECK constraints, unique/filter indexes and requirement-ID tests.
- **Inventory as a working core**: append-only movement ledger with DB-enforced immutability and a non-negative CHECK, per-location balances with pessimistic locking, WAC costing at scale 8, and three real document workflows (transfers with partial receiving and source-WAC preservation, adjustments with audit-only shortfall reconciliation, opening imports with dry-run validation) — 117 tests for this module alone, all passing.
- **Engineering process**: ADR-driven decisions, a decision register, increment logs with evidence, handoff discipline, checkpoint tooling with a 19-case self-test.

### The three most important next steps

1. **Add `tests/Inventory.UnitTests` and `tests/Inventory.IntegrationTests` to `SupplyForge.slnx`** so that `dotnet build`, `dotnet test`, CI and the checkpoint script actually verify the newest work (117 tests). Small change, largest immediate integrity gain.
2. **Obtain the five near-term owner decisions** that unblock A1's gate and A2: ADR-007 (approval engine), ADR-011 (logo), OD-03 (WAC reversal), migration approvals (Q-C, PLT-013, and applying the six pending migration files), and the Q7/MD-003 placement — then execute A1-8 and A1-9 to close the A1 slice.
3. **Produce the module permission matrices (`06b` §6.4.1) and ship the first business endpoint slice** (Master Data CRUD + Inventory document endpoints). Until endpoints exist, the project's core claims — branch isolation 404/403, IDOR fail-closed, permission coverage — are only demonstrable on platform routes, and the product cannot be demoed end-to-end.

### Is the engineering standard appropriate for a serious portfolio backend?

**Yes.** The discipline is unusually strong: requirement-ID traceability in test names, real-database integration tests, architecture tests that encode the ADRs, honest deferral records (blocked items are documented rather than faked), migrations never applied without approval, and progress documents that — with the exceptions listed in §4 item 10 — match the code. The defects found in this audit are *gaps and drift*, not sloppiness: one solution-file omission, several stale documents, and a large amount of unwritten product.

### Portfolio readiness

**Partially portfolio-ready.** The repository is credible and impressive *as an architecture and engineering-process exhibit*: a reviewer can read the module boundaries, the ambient-transaction design, the WAC posting logic and the test suite and conclude the author can design and verify a modular monolith. It is **not yet portfolio-ready as a product demonstration** — there is no runnable business workflow, no business API, no demo data, and the headline lifecycle (supplier → … → payment → report) is 0 % implemented. With A2–A4 plus a permission-matrix/endpoint slice, it would become demonstrable; B1–B4 would make it complete.

---

## Appendix — Audit method and evidence index

**Documents read:** `README.md`, `AGENTS.md`, `docs/HANDOFF.md`, `docs/A0-PROGRESS.md` (increment table + A0-7/8/9 notes + gate review), `docs/A1-PROGRESS.md` (full), `docs/Decision Register.md`, `docs/DATABASE-GUIDE.md` (scope/structure claims), `docs/prd/INDEX.md`, `00-how-to-use.md`, `02a-product-overview.md`, `03-constraints-assumptions-defaults.md`, `05-01-PLT`, `05-02-MD`, `05-03-INV`, `05-04-PRC` (sample), `06b`, `10-testing-acceptance.md`, `11-delivery-plan.md`, `ADR/ADR-001.md`, `.github/workflows/ci.yml`, `docker-compose.yml`, `.gitignore`, `.env.example`.

**Code read:** `StockMovementPoster.cs` (full), `TransferOrchestrator.cs` (ship/receive paths), `Outbox.cs`, `Program.cs`, `PlatformSharedServices.cs` (audit + idempotency), `AuthService.cs` (audit call sites), `PermissionCodes.cs`, `LocationType.cs`, `Product.cs`, `Warehouse.cs`, `StorageLocation.cs`, `StockBalance.cs`, `SharedInfrastructureEntities.cs`, `ThrowawayDatabase.cs` (Inventory), `SupplyForge.slnx`, migration files (table creation counts).

**Commands executed:** `git status`, `git log`, `git show --stat`, `git log -S …`, `dotnet build`, `dotnet test` (root + 2 Inventory projects), `dotnet format --verify-no-changes`, `gh run list`, read-only `sqlcmd` queries against the running SQL Server container, `grep`/`find` across `src`, `tests`, `docs`.

**Not verified (labelled unverified):** individual A0-8 sub-item commits beyond the SEC-20 tests (documented, not re-executed item-by-item); GitHub CI job *step* contents for run `45854768407` (status verified, log contents not inspected); behaviour of the Host against a database that has the Master Data/Inventory migrations applied (no such database exists); `scripts/checkpoint.sh` execution (not run — it can commit/push).
