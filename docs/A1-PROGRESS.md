# A1 Progress — SupplyForge

Slice: **A1 Master + Inventory core** (PRD §11.2). Created: 2026-11-20.
Authority: PRD v0.8 + accepted ADR-001…006, 008, 009. This file tracks increment
status only; it is not a requirements source.
**Active agent:** none · **Last handoff:** 2026-11-24T23:11:00Z (A1-4 Part 1 committed 93b28c0; docs sync; Q-E decided 2026-11-25 option a)

**Files read before planning:** `AGENTS.md` (root), `HANDOFF.md`,
`docs/prd/INDEX.md`, `docs/prd/00-how-to-use.md`, `03-constraints-assumptions-defaults.md`,
`04a-module-map-and-rules.md`, `04c-idempotency-transactions-outbox.md`,
`05-01-platform-core-PLT.md`, `05-02-master-data-MD.md`, `05-03-inventory-INV.md`,
`06a-global-invariants-location-negative-stock.md`, `06d-security-and-privacy.md`,
`09-data.md`, `10-testing-acceptance.md`, `11-delivery-plan.md`,
`ADR/ADR-001.md`, `docs/Decision Register.md`. Library files: **none opened this
slice** — the always-on rules in `my-ai-knowledge/AGENTS.md` (already loaded) cover
this work; no row of its index matched beyond them.

---

## 0. Blocked on the owner (not implemented, not guessed)

A1's acceptance gate in `11-delivery-plan.md` names five items. Three cannot be
built without a mechanism the PRD does not define. Per project `AGENTS.md`
("do not invent the mechanism") and PRD §0 rule 3, these are **documented, not
implemented**, and all unrelated A1 work continues.

| # | Requirement | Blocker | Evidence | Options | Consequence of each option |
|---|---|---|---|---|---|
| B1 | **PLT-030…034 approval engine**, **INV-041** (adjustment above threshold → approval; creator cannot approve) — gate item *"INV-041 via approval engine"* | **ADR-007 not drafted** (`docs/Decision Register.md`: "Not drafted"; `ADR/` has 001–006, 008, 009 only) | PRD `05-01` PLT-030…034; `05-03` INV-041; `11` A1 row; `DM-2` entities `ApprovalPolicy/Step/Request/Decision` exist as names only | (a) Owner drafts/accepts ADR-007 → I implement. (b) Owner approves me drafting ADR-007 as a **proposal** for review first. (c) Ship A1 without INV-041 and re-test the gate later. | (a) unblocks the full gate. (b) fastest, but the mechanism is still owner-approved before code. (c) A1 gate stays partially unevidenced. |
| B2 | **SEC-40** logo upload (authenticated, non-public retrieval, type+size limits) — gate item *"logo upload tests"* | **ADR-011 not drafted** (Decision Register: "Not drafted"; `11` says "logo upload (SEC-40 narrowed scope — ADR-011)") | PRD `06d` §6.7.5 SEC-40; `11` A1 row | (a) Owner drafts/accepts ADR-011 (narrowed scope). (b) Owner approves me drafting it as a proposal. (c) Defer logo to a later slice. | (a)/(b) unblock; (c) gate item unevidenced. |
| B3 | **WAC-07** late-reversal eligibility for stock-movement reversals | **OD-03** open (`05-03` WAC-07 marked *NEEDS OWNER APPROVAL*; `03` TD-5: "reversal eligibility and correction path pending OD-03") | PRD `05-03` WAC-07 + design note (proposed ADR-018 **not accepted**); `10` §10.1 WAC row | (a) Owner decides OD-03 (accept/reject ADR-018). (b) A1 ships WAC without reversal-eligibility rules. | (a) unblocks reversal tests. (b) **WAC-01…06, 08…11 still fully implementable** — only late-reversal *eligibility* is deferred. |

**TD-5 default is implemented** where it is a default and not OD-03: WAC running
average **scale 8** (WAC-09) ships. Only reversal *eligibility* waits on OD-03.

### Conflict documented with a default in force (not blocking)

| # | Requirement | Conflict | Evidence | Options | Consequence of each option |
|---|---|---|---|---|---|
| B4 | **DM-3: surrogate keys are BIGINT** (`09-data.md`, restated in ADR-005) | The A0 platform schema already shipped **Guid** surrogate keys in an applied migration, and branch scoping is typed against it | `UserBranchScope.BranchId` is `Guid` in `20261116221419_InitialPlatformCore`, commented *"Master Data owns Branch entity; A0 stores opaque id only"*; `BranchIsolationTests` (A0, committed) asserts `Guid` branch ids; `BranchIsolationTests`' own header says *"Master Data owns Branch entities later (A1)"* | (a) **Guid surrogate keys everywhere** (default in force). (b) bigint for new tables only, Guid for Branch. (c) convert everything to bigint. | (a) consistent with A0 and with `UserBranchScope`; deviates from DM-3 text. (b) two key styles coexist in one system. (c) requires altering **applied** platform tables and rewriting committed A0 tests — unavailable under this slice's boundary. |

**Default (a) is implemented** in A1-1 (`Company.Id`, `Branch.Id` are `Guid`).
**Owner accepted (a) on 2026-11-24** (A1-3 answer D2): the Decision Register
carries it as **PA-18, a DM-3 amendment** — Guid surrogate keys are the project
key style. B4 stays above as the record of the original conflict; no
conversion is scheduled. Do not silently switch key style mid-slice.

**Not blocked:** everything else in A1. Proceed below.

---

## 1. Increment plan

Order is dependency-driven: module skeleton → master data → inventory ledger →
WAC → documents that post movements.

| # | Increment | Requirement IDs | Status |
|---|---|---|---|
| A1-1 | **Master Data module foundation** — `SupplyForge.MasterData`, `.Contracts`, `.Infrastructure` per ADR-001; `master_data` schema; `Company` singleton + **3-branch seed**; ambient session generalised from 2 → N module contexts (ADR-003 extension, Host-only composition) | MD-001, MD-002, DM-1, A-2, A-4, ADR-001, ADR-003, MOD-1, MOD-5 | **Done 2026-11-20** (evidence §5) |
| A1-2 | **Products + catalogue** — Product (RequiresInspection, reorder, lead time, default price, standard cost), category hierarchy, UoM + conversions, product statuses with no-archive-while-on-hand | MD-010, MD-011, MD-012, MD-013, DM-2, DM-5 | **Done 2026-11-22** (evidence §6) |
| A1-3 | **Partners + shared master** — Supplier + status, Customer + credit limit + status, addresses with defaults, tax rates, payment terms, reason codes. Deactivate-not-delete; price changes never mutate existing docs | MD-020, MD-030, MD-031, MD-050, PLT-075, MD rules | **Done 2026-11-24** (evidence §7) |
| A1-4 | **Warehouses + locations** — branch-owned, the five location types (D-16), block location, no-deactivate-while-holding-stock | MD-040, MD-041, MD-042, DM-1 | **Part 1 committed 2026-11-24 (93b28c0); Part 2 pending (Q-E decided 2026-11-25, option a)** |
| A1-5 | **Inventory ledger + balances** — immutable `StockMovement`, `StockBalance` in the same TXN, reconciliation job comparing balances to movement sums, history queries, opening-balance dry-run | INV-001…004, INV-010…014, INV-012, INV-013, MOD-TXN-01…05, INV-NEG | Planned |
| A1-6 | **WAC core** — bucket = ProductId+WarehouseId, opening blend, receipt blend, issues, transfers, zero-stock, scale 8, no negative average, pool≡ StockValue | WAC-01…06, WAC-08…11 (**not** WAC-07 → B3), INV-080/081 | Planned |
| A1-7 | **Documents that post movements** — adjustments (statuses, reason, threshold), transfers (location move + inter-warehouse states), opening import | INV-030…032, INV-040, INV-042, INV-014 (**INV-041 → B1**) | Planned |
| A1-8 | **Availability + replenishment + returns guard** — availability query with all buckets + incoming, reorder breach alert, Returns-location inbound guard | INV-100, INV-060, INV-061, INV-090, INV-091, `06a` §6.2 | Planned |
| A1-9 | **Admin user lifecycle** — role/branch assignment, last-admin protection | MD-004, BRN-009, BRN-010, PLT-ROLE-3 | Planned |

**MD-003 / MD-004 placement (Q7):** MD-004 is delivered in **A1-9** (row
above). The PRD does not assign MD-003 (Employee directory) to any increment —
**proposal awaiting the owner: deliver MD-003 in A1-9 too** (see §1b "Q7").
Not implemented and not scheduled until the owner approves.

**Deferred by owner decision:** B1 (approval engine / INV-041), B2 (logo / SEC-40),
B3 (WAC-07 / OD-03).

## 1a. A1-2 design decisions (written before any A1-2 code)

Read for this increment: `docs/prd/05-02-master-data-MD.md` (whole file, 25 lines),
`09-data.md` DM-2/DM-4/DM-5, `02b-users-roles-branches.md` §2.7 (branch-scoped vs
company-shared), `04a-module-map-and-rules.md` (Master Data owns products/UoM/
categories). No library file was opened beyond the always-on rules.

The PRD states **what** these four requirements need but not every value, so each
open point is decided here first:

| # | Point | PRD says | Decision | Why |
|---|---|---|---|---|
| D-a | Product identity fields | MD-010 lists "fields **incl.** …" | `Code` (unique SKU), `Name`, `Description` **plus** the five listed fields | "incl." means at least those; a product without a code cannot be referenced by stock, purchasing or sales |
| D-b | Product status values | MD-013 "Product statuses" — no values given anywhere in the PRD (`grep -rn "Archiv" docs/prd/` = 0 hits) | String codes `Active`, `Inactive`, `Archived` (DM-5: string status codes) | Smallest set that expresses MD-013's own vocabulary ("archive"). Assumption, stated per library rule 14 — swap later without a schema change |
| D-c | Status transitions | DM-5 "transitions tested"; MD-013 states exactly one constraint | Any of the three codes → any other code, **except** archive requires `on_hand ≤ 0` | MD-013 gives no other rule; inventing a terminal state would be a requirement the PRD does not have |
| D-d | Archive guard timing | MD-013 "no archive with on-hand > 0" | Rule written as a pure domain function `ProductStatus.CanArchive(onHand)` now, unit-tested now; wired to real `StockBalance` in **A1-5** | On-hand does not exist until the inventory ledger (A1-5) — the rule can be written and tested before its data source can |
| D-e | Category ↔ Product link | MD-011 only says "Category hierarchy" | Self-referencing `Category.ParentId` + nullable `Product.CategoryId` | Without the link a hierarchy has no consumer; nullable keeps uncategorised products legal |
| D-f | Category cycles | not stated | **Not implemented** | No category write service exists yet; adding cycle logic with no caller would be speculative (G-8). Revisit with the category API |
| D-g | UoM conversion scope | MD-012 "UoM conversions; store base unit" | Global `UnitOfMeasureConversion(From, To, Factor)` + required `Product.BaseUnitOfMeasureId` | PRD does not say per-product conversions; global pairs are the simpler reading |
| D-h | Conversion factor sanity | not stated | CHECK constraint `Factor > 0` on the table | A zero/negative factor silently corrupts every quantity conversion (FIN-CALC-04 scale 4) |
| D-i | Company-shared, no branch | `02b` §2.7: "Company-shared master data has no `BranchId`: products, categories, UoM …" | **No `BranchId`** on Product/Category/UoM | Direct PRD statement (MOD-1 + BRN) |
| D-j | Decimal scales | DM-2 / FIN-CALC-04 / A-1 | money `decimal(18,2)`, qty `decimal(18,4)` | DM-2: money per base-currency scale, qty scale 4 default |

**Scope of A1-2:** data model + migration + tests. No HTTP endpoint is added here:
the PRD fixes no permission codes for Master Data (`grep` finds no module matrix),
and PLT-022 requires an explicit `perm:*` policy on every endpoint — inventing
codes would invent requirements. Endpoint work is scheduled as its own increment
once the permission matrix exists.

## 1b. A1-3 design decisions (approved by the owner before any A1-3 code)

Read for this increment: `docs/prd/INDEX.md`, `00-how-to-use.md`, Core set
(`03`, `04a`, `04b`, `04c`, `06a`, `02b`), slice files `05-01`, `05-02`, `06d`,
`09`, plus `06c` (tax scale), `10`, `11`, `12`; greps only of `05-03/04/05/06`,
`13`, `appendix-c`. Code read: `MasterDataDbContext`, `Product`,
`UnitOfMeasure`, `ProductStatus`, `MasterDataSeed`, the A1-2 migration and
tests. Library: `workflows/planning.md`. The STEP-1 plan was presented and the
owner approved it on 2026-11-24 with the answers below.

### Decisions, as approved (D1–D17)

| # | Point | Decision |
|---|---|---|
| D1 | Contacts (`SupplierContact`/`CustomerContact` exist in DM-9.2 only) | **Out of scope** — no requirement ID covers them (owner answer Q4: deferred) |
| D2 | Key style | **Guid** (owner accepted 2026-11-24 as the project key style; DM-3 amendment recorded in `docs/Decision Register.md` PA-18 — B4 above stays as the conflict note) |
| D3 | Address shape | Two tables (`SupplierAddress`, `CustomerAddress`) per DM-9.2 |
| D4 | Status values | Supplier: `Active`/`Inactive`/`Blocked`. Customer: `Active`/`Inactive`/`Blocked`/`OnHold` — smallest set carrying the PRD's own words |
| D5 | Lookup vs partner status | `IsActive` bit on TaxRate/PaymentTerm/ReasonCode (A1-1/A1-2 lookup convention); `Status` string code on Supplier/Customer (DM-5) |
| D6 | Address default flags | `IsDefaultShipping` + `IsDefaultBilling`, each with a unique filtered index (owner answer Q2: approved) |
| D7 | Address columns | Line1/Line2/City/Region/PostalCode/Country; Line1+City+Country required; PostalCode nullable |
| D8 | `CreditLimit` type | `decimal(18,2)`; NOT NULL with database default 0 (owner answer Q1: 0 = no credit, no unlimited value in P1, TD-3 fails closed) |
| D9 | ReasonCode shape | One global list, unique `Code`; **a purpose column can be added later** (owner answer Q5: approved; noted, deliberately not built) |
| D10 | `TaxRate.Rate` | `decimal(9,6)` stored as a percentage (FIN-CALC-10) |
| D11 | `PaymentTerm.DueDays` | `int`, required (FIN-021) |
| D12 | "price changes never mutate existing docs" | **Nothing to build in A1-3** — partner tables hold no prices; enforced later by frozen document snapshots (FIN-CALC-11/12, SAL-024, DM-4) |
| D13 | "Blocked supplier/customer rules" | Store the status only; behaviour lands in A2 (supplier) / A3 (customer). Supplier-side Blocked rule does not exist in the PRD (owner answer Q3: park to A2; no approval logic now) |
| D14 | HTTP endpoints | None — no Master Data permission codes exist (PLT-022 forbids inventing them), same scope call as A1-2 |
| D15 | Seed rows | None (owner answer Q6: no seed rows; data, not schema) |
| D16 | Migration handling | Migration **file** only; `dotnet ef database update` never run; tests on throwaway databases (§3 constraint 1) |
| D17 | Delete behavior | Both FKs `DeleteBehavior.Restrict` (PLT-075, MD rules, A1-2 zero-cascade rule) |

### Plan changes the owner required (all three implemented)

1. **FK indexes — my STEP-1 statement was wrong.** The A1-2 migration does have
   EF-convention FK indexes (`IX_Product_CategoryId`, `IX_Category_ParentId`,
   `IX_Product_BaseUnitOfMeasureId`, `IX_UnitOfMeasureConversion_ToUnitId`).
   The convention is **not suppressed** in A1-3: it produced the composite
   default indexes `IX_SupplierAddress_SupplierId_IsDefaultShipping` /
   `_IsDefaultBilling` and the customer equivalents — their **leftmost column
   is the FK**, so SQL Server seeks them for parent lookups, which is why EF
   creates no additional single-column index on the same column (the same rule
   that produced `IX_UnitOfMeasureConversion_ToUnitId` but no `_FromUnitId` in
   A1-2). Asserted in `MIGRATION_A1PartnersSharedMaster_…`.
2. **Three CHECK constraints**, same rationale as A1-2's `Factor > 0` (bad
   values silently corrupt data): `CK_Customer_CreditLimit_NonNegative`,
   `CK_TaxRate_Rate_Between0And100`, `CK_PaymentTerm_DueDays_NonNegative` —
   one schema test each.
3. **Filtered-index test:** a second default-shipping address for the same
   supplier must fail while non-default addresses pass (and the same for the
   billing flag and the customer side).

### Q7 — where MD-003 and MD-004 are delivered

- **MD-004 (admin user lifecycle + role/branch assignment): A1-9.** Already
  decided — its requirement IDs sit in that row of §1 (MD-004, BRN-009,
  BRN-010, PLT-ROLE-3).
- **MD-003 (Employee directory, optional user link): the PRD does not decide
  an increment for it** — §11.2's A1 row does not enumerate increments, and no
  other increment claims MD-003. **Proposal, waiting for the owner: deliver
  MD-003 in A1-9** (an employee directory with an optional user link is the
  same subject as users, roles and branch assignment; A-5 keeps it non-HR).
  **Not implemented and not scheduled until the owner approves this placement.**

## 1c. A1-4 design decisions (owner answers 2026-11-24, written before code)

Read for this increment: `docs/prd/INDEX.md`, `00-how-to-use.md`, Core set
(`03`, `04a`, `04b`, `04c`, `06a`, `02b`), slice files `05-02`, `09`, plus
`06a` §6.2 and `04a` MOD-10. Code read: `MasterDataDbContext`, `Branch`,
`Product`, the A1-3 migration and tests, `UserBranchScope`,
`PlatformCoreDbContext`, `ThrowawayDatabase`. Library: none beyond the
always-on rules.

### Decisions, as approved

| # | Point | Decision |
|---|---|---|
| D1 | Branch scoping mechanism | **One mechanism.** Every future stock table must carry `WarehouseId` directly. |
| D2 | MD-042 / Type immutability without a cycle | MasterData must **not** call Inventory. MasterData owns the port `ILocationDeactivationGuard` in `SupplyForge.MasterData.Contracts`; Inventory implements it in **A1-5**. A1-4 ships the port + a public placeholder that allows everything (documented); A1-5 replaces it. |
| D3 | CreatedAt/UpdatedAt | `datetimeoffset`, set from `TimeProvider`, **no SQL default** (same as A1-3). |
| D4 | Delete | **No delete operation at all.** `Restrict` only blocks deleting a parent with children. Cross-module references are plain `Guid`s with **no FK** (DM-4). |
| D5 | `Warehouse.Code` | Unique **company-wide**, **non-filtered** (Q-A). A deactivated warehouse keeps its code reserved. |
| D6 | Branch scope | **Not** `AmbientDbSession`. `ICurrentBranchScope` on the Platform Core side, fed by `UserBranchScope` rows + the BRN-010 company-wide flag. EF global query filters read a **property on the DbContext** (not a captured service). Design-time factory keeps working. Out-of-scope create rejected; company-wide sees all; a user with no branches sees nothing. **Part 2 only — not implemented in Part 1.** |

### Q-A…Q-E answers

- **Q-A** non-filtered `Warehouse.Code` unique.
- **Q-B** entity/table name **`StorageLocation`** (DM-9.2).
- **Q-C** `IsCompanyWide` on `platform.User` in **Part 2**, migration **file only**. BRN-010 text (`02b:67`): "Users assigned one+ branches or company-wide scope flag."
- **Q-D** `ICurrentBranchScope` = `BranchIds` + `IsCompanyWide` + `CanAccess(Guid)`.
- **Q-E** **decided 2026-11-25 (option a)**: explicit system scope for seed, migration and later background jobs; fail closed by default; never reachable from HTTP. Part 2 not started.

### Removed constraint (not PRD-required)

`CK_StorageLocation_IsBlocked_Implies_Inactive` is **removed**. No PRD line
requires it. The only relevant text is MD-042 "Block location; no deactivate
while holding stock" (`05-02:20`) and §6.2 "Storage … Yes **if not blocked**"
/ "Quarantine … **Always blocked**" (`06a:26,29`). A blocked Storage location
can still hold stock and stays active.

### Part 1 scope

Tables + migration **file only** + unit/integration tests on throwaway
databases + docs. No HTTP endpoint (no Master Data permission codes; PLT-022).
No commit/push in Part 1 (owner: review first). Part 2 (branch scope) is
**not** implemented yet.

## 2. Acceptance gate mapping (PRD §11.2 A1 row)

| Gate item | Served by | State |
|---|---|---|
| Reconciliation | A1-5 (INV-011 job: balances ≡ movement sums) | Planned |
| INV-004 tests | A1-5 (`on_hand ≥ 0`, `available ≥ 0`, no double-reserve) | Planned |
| WAC-02/03/08 tests | A1-6 | Planned |
| INV-041 via approval engine | **B1 — blocked on ADR-007** | Blocked |
| Logo upload tests | **B2 — blocked on ADR-011** | Blocked |

## 3. Constraints that shape every increment

1. **The dev database is never modified and no migration is ever applied to it**
   (owner instruction, this slice). Consequences:
   - New-schema tests run against a **throwaway database** created and dropped by
     the test (pattern proven by `NotificationDeliveryMigrationTests`): `CREATE
     DATABASE` → `IMigrator.Migrate()` → assert → `DROP DATABASE`.
   - `dotnet ef migrations add` may be used to **create migration files only**;
     `dotnet ef database update` is never run.
   - Existing dev-DB test suites must keep passing unchanged — an endpoint that
     needs a not-yet-applied table is not added to those suites.
2. **No package upgrades.** New projects use the versions already in the tree
   (EF Core 10.0.15, `Microsoft.NET.Sdk`, `net10.0`).
3. **No commits or pushes** without explicit owner authorisation.
4. **No test is weakened, skipped or deleted** to make a change pass.
5. **MOD-1 / MOD-1a / MOD-5 / MOD-10:** MasterData and Inventory own their
   tables; cross-module access is contract-only; `MOD-10` requires the
   architecture tests to be **extended** for the new module graph (not loosened).
6. **Traceability:** requirement ID in every test name (NFR-051).

## 4. Architecture notes for A1-1

- Layout follows the **accepted** ADR-001 recommendation table:
  `src/Modules/MasterData/SupplyForge.MasterData{,.Contracts,.Infrastructure}/`,
  named `SupplyForge.MasterData*`. Same for Inventory later.
- Contract assemblies are **leaves** (ADR-001 §PRD-confirmed 3); the existing
  `PLT_043_ContractsAreLeaf_NoOutwardReferences` test gains the new assembly names.
- The ambient session (`Host/Data/AmbientDbSession.cs`) currently hardcodes two
  contexts. A1-1 generalises it to enlist **every** registered module context on
  one shared connection/transaction (ADR-003 unchanged in substance: still one
  SQL Server, no distributed transactions — MOD-TXN-01/04). Host stays the only
  composition root (`MOD_001_HostIsOnlyCompositionRoot`).
- `MOD-1_SharedSchemasAreSeparate_ContextsNotShared` gains the `master_data`
  schema assertion.

## 5. A1-1 evidence (completed 2026-11-20)

**Files created**

| Path | Purpose |
|---|---|
| `src/Modules/MasterData/SupplyForge.MasterData.Contracts/` | Leaf contracts assembly (marker only — nothing to contract yet) |
| `src/Modules/MasterData/SupplyForge.MasterData/Domain/{Company,Branch}.cs` | MD-001 company singleton, MD-002 branch |
| `src/Modules/MasterData/SupplyForge.MasterData.Infrastructure/Data/MasterDataDbContext.cs` | `master_data` schema owner (MOD-1) |
| `.../Data/MasterDataDbContextFactory.cs` | design-time factory for `dotnet ef migrations add` (never opens a connection) |
| `.../Data/MasterDataSeed.cs` | idempotent first-run seed (MD-001/MD-002), schema-absent → `false` |
| `.../Data/Migrations/20261119232720_A1InitialMasterData.cs` | creates schema + `Company` + `Branch` + 2 indexes; `Down` drops exactly those |
| `tests/MasterData.IntegrationTests/` | 10 tests, all on throwaway databases |

**Files changed:** `SupplyForge.slnx` (+4 projects), `Host/SupplyForge.Host.csproj`
(+1 reference), `Host/ServiceCollectionExtensions.cs` (DbContext + seed + 3-context
session factory), `Host/Program.cs` (seed call with logged skip),
`Host/Data/AmbientDbSession.cs` (2 → N contexts, same-string guard),
`tests/ArchitectureTests/*` (MOD-10 extension), `.github/workflows/ci.yml`
(+1 `dotnet ef database update` line for the **ephemeral CI container only**).

**Tests added (NFR-051 — requirement ID in every name)**

| Test | Requirement |
|---|---|
| `MD_001_Seed_CreatesTheCompanySingleton_WithEveryProfileField` | MD-001 |
| `MD_001_TwoActiveCompanies_AreRejectedByTheSchema` | MD-001 (schema-enforced "exactly one active") |
| `MD_002_Seed_CreatesExactlyThreeBranches_WithUniquePlaceholderCodes` | MD-002, A-2, Q-3 |
| `MD_001_MD_002_SeedRunTwice_CreatesNoDuplicates` | MD-001/MD-002 idempotency |
| `MD_002_DuplicateBranchCode_IsRejectedByTheUniqueIndex` | MD-002 |
| `MD_001_SeedWithoutTheMigration_ReportsSchemaAbsent_AndWritesNothing` | MD-001 first-run, Host safety |
| `MIGRATION_A1InitialMasterData_UpCreatesSchemaTablesAndIndexes_DownReversesExactly` | migration boundary |
| `MOD_TXN_01_Session_JoinsAllThreeModuleContextsOnOneConnection_ThenRestoresThem` | MOD-TXN-01 |
| `MOD_TXN_04_SessionCommit_PersistsRowsFromAllThreeModuleContexts` | MOD-TXN-04 |
| `MOD_TXN_05_SessionRollback_LeavesNoRowInAnyModule_ContextsRemainUsable` | MOD-TXN-05 |

**Extended, not weakened:** `PLT_043_ContractsAreLeaf` (+`MasterData.Contracts`,
other contracts now forbidden too), `MOD_001_ModulesDoNotReferenceOtherModuleImplementations`
(generalised to every module pair), `MOD_005_SharedDoesNotReferenceModules`,
`MOD_008_BannedInfrastructurePackages`, `MOD_001_HostIsOnlyCompositionRoot`,
`MOD_1_SharedSchemasAreSeparate_ContextsNotShared` (+`master_data`, +3-way
context distinctness). No assertion was removed or loosened.

**Verification run (2026-11-20)**

- `dotnet build` → 0 errors, 0 warnings.
- `dotnet test` → **179/179**: architecture 14, unit 68, PlatformCore
  integration 87, **MasterData integration 10** (was 169 before A1-1).
- `dotnet format --verify-no-changes` → exit 0.
- Host started against the development database: starts, logs exactly one
  warning (`Master Data seed skipped: the master_data schema is not present…`)
  and serves `/health` — **no `fail:`/`error` lines, no write to the dev DB**.

**Decisions taken inside A1-1 (ordinary, within PRD/ADR scope)**

1. `Branch.Id`/`Company.Id` are `Guid` → **B4** above (DM-3 conflict, default in force).
2. Company singleton is enforced twice: fixed sentinel PK **and** a unique
   filtered index on `IsActive` (`[IsActive] = 1`) — MD-001 is a schema invariant.
3. Placeholder seed values: branch codes/names `BR-01…03`/`Branch 01…03` (Q-3
   explicitly allows placeholders) and legal name `"SupplyForge Demo Company"`
   (MD-001 states the field but gives no default — no schema change needed later).
4. The seed runs at Host startup like `PlatformCatalogueSeed` (A0 convention:
   runtime seed, no `HasData`, no migration approval needed for data). It **skips
   with a warning** when `master_data` is absent, so a pre-A1 database still boots.
5. CI gained the third `dotnet ef database update` line. It targets only that
   job's own ephemeral service container (workflow header, lines 9–10) — never the
   development database. Not yet executed on GitHub (nothing is committed/pushed).

**Known issues recorded, not silently fixed**

- `OUT_01_Outbox_Enqueue_PersistsPendingMessage` uses the same
  "latest row in a shared table" pattern that made `PLT_040` flaky. Left
  untouched (out of scope); recommend the identical entity-id scoping fix.
- The failed `PLT_040` run before the fix left one test row in
  `platform.AuditLog` on the dev database (`ActorId='test'`,
  `Action='test.action'`, `EntityId='1'`). Not deleted — removing data needs
  owner approval. It does not affect any assertion.
- No HTTP endpoint was added: MD-001 explicitly says company creation is "not a
  tenant API", and A1-1's gate items are data/ledger concerns of later increments.

---

## 6. A1-2 evidence (completed 2026-11-22)

Decisions were written first in §1a (D-a…D-j). Scope stayed data model +
migration + tests — no HTTP endpoint (permission matrix does not exist yet, see
§1a scope note).

**Files created**

| Path | Purpose |
|---|---|
| `src/Modules/MasterData/SupplyForge.MasterData/Domain/{Product,ProductStatus,Category,UnitOfMeasure,UnitOfMeasureConversion}.cs` | MD-010…MD-013 entities + status-code rules (DM-5) |
| `.../Infrastructure/Data/MasterDataDbContext.cs` (extended) | 4 new DbSets; unique codes, self-ref Category, FKs, `HasPrecision`, `CK_UnitOfMeasureConversion_Factor_Positive` |
| `.../Data/Migrations/20261120110027_A1ProductCatalogue.cs` | Up: Category, UnitOfMeasure, Product, UnitOfMeasureConversion + indexes + check; Down drops exactly those 4 |
| `tests/MasterData.UnitTests/` | new unit project (ADR-001 naming) + slnx entry — `ProductStatus` rules, no DB needed |
| `tests/MasterData.IntegrationTests/ProductCatalogueTests.cs` | 9 tests, all on throwaway databases |

**Tests added (NFR-051 — requirement ID in every name)**

| Test | Requirement |
|---|---|
| `MD_013_ProductStatuses_AreExactlyTheThreeDocumentedCodes` … `MD_013_Product_DefaultsToTheActiveStatusCode` (6 unit tests) | MD-013, DM-5, MD-010 default |
| `MD_010_Product_PersistsEveryRequiredField` | MD-010, DM-2, A-1 (scales) |
| `MD_010_ProductCode_IsUnique` | MD-010 |
| `MD_011_CategoryHierarchy_PersistsParentAndChildLinks` | MD-011 (any depth) |
| `MD_011_ProductMayBeLeftUncategorised` | MD-011, D-e |
| `MD_012_UoMConversion_PersistsFactor_AndRejectsASecondPair` | MD-012 (unique pair) |
| `MD_012_UoMConversion_NonPositiveFactor_IsRejectedByTheSchema` | MD-012, D-h (schema-enforced) |
| `MD_013_ProductStatus_IsStoredAsStringStatusCode` | MD-013, DM-5 persistence |
| `DM_2_CatalogueColumns_AreStoredAtTheirDeclaredPrecision` | DM-2 (asserted against live `sys.columns`, not the EF model) |
| `MIGRATION_A1ProductCatalogue_UpCreatesCatalogueTablesAndGuard_DownReversesExactly` | migration boundary (6 tables up → 0 down) |

**Decisions taken inside A1-2 (ordinary, within PRD/ADR scope)**

1. **All five FKs of the new tables are `DeleteBehavior.Restrict` — zero
   cascades.** The first migration attempt used EF's convention (required
   reference → cascade) and **could not be applied**: SQL Server error 1753,
   *"multiple cascade paths"* (`UnitOfMeasure` → conversion via From + To).
   The model fix also matches PRD `05-02`'s rule **"deactivate not delete"**:
   a category or base unit in use must be deactivated, never deleted out from
   under its products. The broken migration was never applied to any database;
   it was removed with `dotnet ef migrations remove` and re-scaffolded
   (`20261119235652` → `20261120110027`). `dotnet ef database update` was
   never run (§3 constraint 1).
2. **A1-1's migration test was de-coupled from later increments.** It asserted
   a hard-coded total of 2 tables in `master_data`, which A1-2's four tables
   invalidated (the one failing test of the first A1-2 run — not a product
   defect). The assertion now checks A1-1's own two tables **by name**
   (`IN ('Company','Branch')`); the schema total is asserted only by the
   newest migration test (A1-2 asserts 6). No assertion was removed or
   loosened — this is the same total-count check scoped to its owner.

**Verification run (2026-11-22)**

- `dotnet build` → 0 errors, 0 warnings.
- `dotnet test` → **194/194**: architecture 14, unit **74** (68 + 6 new),
  PlatformCore integration 87, **MasterData integration 19** (10 A1-1 + 9
  A1-2) — two consecutive full green runs.
- `dotnet format --verify-no-changes` → exit 0.
- One intermediate full run showed 4 PlatformCore + 6 MasterData failures
  while a `dotnet format` process ran **concurrently** (load-induced; failure
  messages were not captured before the grep discarded them — recorded here
  rather than claimed as understood). Strictly sequential re-runs were green
  and standalone MasterData runs were 19/19; the throwaway-DB names are
  GUID-based, so no test-name collision exists. Not reproduced since.

**Not done in A1-2 (by design):** HTTP endpoints (no Master Data permission
codes exist — PLT-022 forbids inventing them); category-cycle logic (D-f, no
caller yet); wiring `CanArchive` to real on-hand (D-d → A1-5).

## 7. A1-3 evidence (completed 2026-11-24)

Decisions were written first in §1b (D1–D17) and approved by the owner
together with the STEP-1 plan; Q1–Q7 answers are folded into that table.
Scope stayed data model + migration + tests — no HTTP endpoint (D14), no seed
rows (D15).

**Files created**

| Path | Purpose |
|---|---|
| `src/Modules/MasterData/SupplyForge.MasterData/Domain/{Supplier,SupplierStatus,Customer,CustomerStatus}.cs` | MD-020/MD-030 partner entities + status-code rules (DM-5) |
| `.../Domain/{SupplierAddress,CustomerAddress}.cs` | DM-9.2 addresses with the two default flags (D6) |
| `.../Domain/{TaxRate,PaymentTerm,ReasonCode}.cs` | shared master lookups (MD-031, MD-050, FIN-021) |
| `.../Infrastructure/Data/MasterDataDbContext.cs` (extended) | 7 new DbSets; 3 CHECKs; 4 filtered unique indexes; Restrict FKs; `CreditLimit` precision(18,2) default 0; `Rate` precision(9,6) |
| `.../Data/Migrations/20261124011532_A1PartnersSharedMaster.cs` (+`.Designer.cs`, snapshot) | Up: 7 tables + indexes + checks; Down drops exactly those 7 |
| `tests/MasterData.UnitTests/PartnerStatusTests.cs` | 11 status-rule tests, no DB needed |
| `tests/MasterData.IntegrationTests/PartnersAndSharedMasterTests.cs` | 17 tests, all on throwaway databases |

**Files changed:** `tests/MasterData.IntegrationTests/ProductCatalogueTests.cs`
(A1-2 migration test de-coupled by name — see below).

**Tests added (NFR-051 — requirement ID in every name)**

| Test group | Requirement |
|---|---|
| 11 unit tests: supplier/customer status-code sets, defaults, transitions | MD-020, MD-030, DM-5 |
| `MD_020_Supplier_PersistsEveryRequiredField` / `MD_030_Customer_…` | MD-020, MD-030, DM-9.1 |
| `MD_020_SupplierCode_IsUnique` / `MD_030_CustomerCode_IsUnique` | MD-020, MD-030 |
| Address persistence + `MD_020_…_IsDefaultShipping` / `IsDefaultBilling` unique-filtered tests (2 defaults fail, 2 non-defaults pass) | DM-9.2, D6, owner plan change 3 |
| `MD_031_TaxRate_…`, `MD_031_TaxRate_Rate_IsBetween0And100_InTheDatabase` (CHECK) | MD-031, D10, owner plan change 2 |
| `MD_050_PaymentTerm_…`, `MD_050_PaymentTerm_DueDays_NonNegative_InTheDatabase` (CHECK) | MD-050, D11, owner plan change 2 |
| `MD_030_Customer_CreditLimit_DefaultsToZero_AndRejectsNegative` (CHECK) | MD-030, D8, owner plan change 2, TD-3 |
| `MD_050_ReasonCode_…` global list, unique `Code` | MD-050, D9 |
| `MIGRATION_A1PartnersSharedMaster_UpCreatesSevenTablesAndGuards_DownReversesExactly` | migration boundary (13 tables up → 6 down; FK-index rule asserted) |

**Decisions taken inside A1-3 (ordinary, within PRD/ADR scope)**

1. **A1-2's migration test was de-coupled from later increments.** Its
   assertion changed from "total = 6 tables" to A1-2's own six tables **by
   name** — the same fix A1-2 applied to A1-1. The schema total (13) is now
   owned by A1-3's migration test. Nothing was removed; all six tables must
   still exist.
2. **No single-column `IX_SupplierAddress_SupplierId` is emitted, and none is
   suppressed.** EF's convention produced the composite default indexes whose
   **leftmost column is the FK** (`(SupplierId, IsDefaultShipping)`), so SQL
   Server can seek them for parent lookups; the convention therefore creates
   no second index on the same column — the same rule that gives A1-2
   `IX_UnitOfMeasureConversion_ToUnitId` but no `_FromUnitId`. Asserted in the
   migration test.

**Verification run (2026-11-24)**

- `dotnet build` → 0 errors, 0 warnings.
- `dotnet test` → **230/230**: architecture 17, PlatformCore unit 68,
  **MasterData unit 17** (6 + 11), PlatformCore integration 92,
  **MasterData integration 36** (19 + 17) — two consecutive full green runs
  (`/tmp/test-run-a1-3-1.txt`, `/tmp/test-run-a1-3-2.txt`).
- `dotnet format --verify-no-changes` → exit 0.

**Not done in A1-3 (by design):** HTTP endpoints (D14); seed rows (D15);
supplier/customer approval or Blocked *behaviour* (D13 → A2/A3); contacts
(D1 → deferred); a `Purpose` column on `ReasonCode` (D9 → may be added later).

---

## 8. A1-4 Part 1 evidence (2026-11-24, committed 93b28c0)

**Files created**

| Path | Purpose |
|---|---|
| `src/Modules/MasterData/SupplyForge.MasterData/Domain/Warehouse.cs` | MD-040 branch-owned warehouse entity |
| `src/Modules/MasterData/SupplyForge.MasterData/Domain/StorageLocation.cs` | MD-041/042 location entity with `Type` (string) |
| `src/Modules/MasterData/SupplyForge.MasterData/Domain/LocationType.cs` | MD-041 static class with 5 string constants: `Receiving`, `Quarantine`, `Storage`, `Returns`, `Staging` (D-16); `Blocked` is a separate `IsBlocked` flag (MD-042) |
| `src/Modules/MasterData/SupplyForge.MasterData.Contracts/Location/ILocationDeactivationGuard.cs` | Port for Inventory to implement in A1-5 (D2) |
| `src/Modules/MasterData/SupplyForge.MasterData.Infrastructure/Location/DefaultLocationDeactivationGuard.cs` | Public placeholder — allows all deactivations (D2) |
| `src/Modules/MasterData/SupplyForge.MasterData.Infrastructure/Data/Migrations/20261124181017_A1WarehousesLocations.cs` | Creates `master_data.Warehouse` + `master_data.StorageLocation`; `Warehouse.Code` unique non-filtered (D5); `Type` CHECK constraint (5 values per D-16); FKs `Restrict` (D4) |
| `tests/MasterData.UnitTests/StorageLocationTests.cs` | 7 unit tests: type values, five-type set, blocked-but-active is legal |
| `tests/MasterData.IntegrationTests/WarehouseLocationTests.cs` | 12 integration tests on throwaway DBs: persistence, unique code, FK restrict, CHECK, placeholder guard |

**Files changed**

| Path | Change |
|---|---|
| `src/Modules/MasterData/.../MasterDataDbContext.cs` | +2 DbSets; `Warehouse.Code` unique index (non-filtered); `StorageLocation.Type` CHECK; `Warehouse.BranchId` FK Restrict; `StorageLocation.WarehouseId` FK Restrict |
| `.../Migrations/MasterDataDbContextModelSnapshot.cs` | Updated to include Warehouse + StorageLocation |
| `src/Host/SupplyForge.Host/ServiceCollectionExtensions.cs` | Registers `DefaultLocationDeactivationGuard` as `ILocationDeactivationGuard` |
| `tests/MasterData.IntegrationTests/PartnersAndSharedMasterTests.cs` | A1-3 migration test de-coupled by table name (own 13 tables) |

**Decisions (recorded in §1c, Decision Register §8)**

- Q-A: `Warehouse.Code` unique company-wide, non-filtered
- Q-B: Entity/table name `StorageLocation` (DM-9.2)
- Q-C: `IsCompanyWide` on `platform.User` in Part 2, migration file only
- Q-D: `ICurrentBranchScope` = `BranchIds` + `IsCompanyWide` + `CanAccess(Guid)`
- Q-E: **Open** — null scope fails closed for requests; seed/migration retain access
- D1: One branch-scoping mechanism; every stock table carries `WarehouseId`
- D2: MasterData owns `ILocationDeactivationGuard`; Inventory implements in A1-5
- D3: `CreatedAt`/`UpdatedAt` = `datetimeoffset`, `TimeProvider`, no SQL default
- D4: No delete; `Restrict` only; cross-module refs are plain `Guid`, no FK
- D5: `Warehouse.Code` unique company-wide, non-filtered
- D6: Branch scope via `ICurrentBranchScope` + DbContext property (not `AmbientDbSession`); Part 2 only
- Removed: `CK_StorageLocation_IsBlocked_Implies_Inactive` (not PRD-required)

**Verification (A1-4 Part 1)**

- `dotnet build` → 0 errors, 0 warnings
- `dotnet test` → **249/249** (architecture 17, PlatformCore unit 68, MasterData unit 24, PlatformCore integration 92, MasterData integration 48), **two consecutive runs**
- Owner re-ran the full sweep 2026-11-25 → build 0 errors, 249/249 passed (MasterData.UnitTests 24, Architecture 17, PlatformCore.UnitTests 68, MasterData.IntegrationTests 48, PlatformCore.IntegrationTests 92), `dotnet format` exit 0, docker healthy; saved `/tmp/test-run-a1-4-verify.txt`
- `dotnet format --verify-no-changes` → exit 0
- Migration file `20261124181017_A1WarehousesLocations` created; **NOT applied** (owner approval required)
- **Committed and pushed** — `93b28c0` (A1-4 Part 1), `e67007c` + `eb5d9a0` (docs sync)

## Handoff log

| Date | Agent | Increment | Files (changed/new/deleted) | Decisions | Build / Test / Format | Migration | Git status | Commit/push |
|---|---|---|---|---|---|---|---|---|
| 2026-11-24 | OpenCode + Kilo (multiple agents) | A1-4 Part 1 | new: Warehouse, StorageLocation, LocationType, port, guard, migration, 2 tests; mod: DbContext, snapshot, Host DI, A1-3 migration test, A1-PROGRESS, 2 HANDOFFs; del: none | Q-A…Q-D accepted; Q-E open; blocked⇒inactive removed | 0/0 · 249/249 ×2 (owner-reported) · clean | created, not applied | 11 mod, 11 untracked, 0 staged (at handoff) | **committed 93b28c0; pushed e67007c** |
