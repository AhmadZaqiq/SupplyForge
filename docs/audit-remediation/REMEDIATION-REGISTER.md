# Remediation Register — SupplyForge Independent Audit

Source audit: `docs/PROJECT_COMPLETION_AUDIT.md` (2026-11-26, `main` @ `29d65ba`).
Register created: 2026-11-26 by the remediation session. Every finding was
re-verified against the current repository before classification; the audit's
severity was re-judged independently.

**Status legend:** CONFIRMED (verified, fix authorized) · ALREADY FIXED ·
PARTIALLY FIXED · INCORRECT (evidence does not support the finding as stated) ·
BLOCKED (owner decision / external dependency) · DEFERRED (valid, out of this
remediation's authorized scope).

Baseline at register creation: `main` @ `29d65ba`, `origin/main` up to date,
working tree = modified `opencode.json` (pre-existing, untouched) + untracked
`docs/PROJECT_COMPLETION_AUDIT.md`. `dotnet build` 0/0; 384/384 tests pass
(267 root-solution + 45 Inventory unit + 72 Inventory integration run
explicitly); `dotnet format --verify-no-changes` exit 0.

---

## A. Solution / test coverage

| ID | Finding (audit §) | Severity (validated) | Evidence | Root cause | Remediation | Status |
|---|---|---|---|---|---|---|
| R-01 | `SupplyForge.slnx` omits `tests/Inventory.UnitTests` and `tests/Inventory.IntegrationTests` (§1, §4.9, §10 Critical) | **High** (not Critical: the tests pass when run — the defect is that CI/build/checkpoint never run them) | `SupplyForge.slnx` lists 5 of 7 test projects; `git log -S "Inventory.IntegrationTests" -- SupplyForge.slnx` empty; root `dotnet test` = 267 tests, explicit Inventory runs = 117 more | Solution file updated for Inventory *source* projects in A1-5 but the two test projects were never added | Add both projects in the existing `/tests/<Name>/` folder convention; verify root build/test/format/CI include them | **CONFIRMED → fixed in this session** |
| R-02 | CI green without Inventory tests (§4.9, §7 CI row) | High (consequence of R-01) | `.github/workflows/ci.yml` runs `dotnet build` + `dotnet test` at root → solution-driven | Same as R-01 | No YAML change required: once R-01 lands, the existing CI steps cover them. CI migration steps are unchanged (Inventory integration tests apply their own migrations to throwaway databases) | **CONFIRMED → resolved by R-01** (actual GitHub run only verifiable after the owner pushes) |
| R-03 | `scripts/checkpoint.sh --verify-only` never ran Inventory tests (§1) | Low | `checkpoint.sh` default `VERIFY_CMD="dotnet build"` (overridable by `SF_VERIFY_CMD` / `--verify-cmd`) | Default verify command is build-only by design | After R-01 the default build compiles both projects. Running tests at checkpoint is an owner-configurable choice (`SF_VERIFY_CMD='dotnet test'`); script not modified | **PARTIALLY FIXED** (build coverage yes; test execution remains opt-in by design) |

## B. Documentation integrity

| ID | Finding (audit §) | Severity | Evidence | Root cause | Remediation | Status |
|---|---|---|---|---|---|---|
| R-04 | `docs/HANDOFF.md` §2 says A1-4P2→A1-7 "NOT committed" (§1, §4.10a) | High | `git log`: `29d65ba` contains all of it and is pushed | Handoff refreshed before the owner's commit, never after | Refresh §2 "Current handoff" with the true state (commit, tree, this remediation session) | **CONFIRMED → fixed in this session** |
| R-05 | README: "seven location types (… Picking, Shipping … Blocked)" (§4.10b) | Medium | `LocationType.cs` has 5 constants (D-16, MD-041); `Blocked` is `IsBlocked` flag (MD-042); PRD `05-02` line 19 | README written before D-16 was settled; never corrected | Correct README to the five types + blocked flag | **CONFIRMED → fixed** |
| R-06 | README: StockBalance "unique on (WarehouseId, ProductId)" (§4.10c) | Medium | `InventoryDbContext.cs:76` `HasKey(ProductId, WarehouseId, LocationId)`; README line 136 already says three columns | Copy from an earlier draft | Correct README line 84 | **CONFIRMED → fixed** |
| R-07 | README "Incomplete / Blocked" table still says A1-4P2…A1-7 are "Completed, uncommitted / Awaiting owner commit" (found during remediation, beyond audit) | Medium | `git log 29d65ba`; README lines ~345–351 | Same cause as R-04 | Update rows to "committed in 29d65ba" | **CONFIRMED → fixed** |
| R-08 | `docs/DATABASE-GUIDE.md`: "32 tables", "Inventory … has no tables yet" (§4.10d) | Medium | 8 `inventory` tables exist in 2 migration files (`20261125171326_A1InventoryLedger`, `20261126004151_A1TransferOrchestration); 40 designed total | Guide (D0) written before A1-5/A1-7 | Add an Inventory section (8 tables, clearly marked *migration files only, not applied to this database*), update counts (40 across 4 modules), fix quick reference | **CONFIRMED → fixed** |
| R-09 | `docs/A1-PROGRESS.md` §1 A1-5 row marked "Done" although reconciliation job + history queries were deferred (§4.1, §7) | Medium | Row text vs §10 "Not done in A1-5" list | Row written at planning time; deferral noted only in §10 | Row status → "Done with deferrals (evidence §10)" naming the deferred items | **CONFIRMED → fixed** |
| R-10 | `docs/A1-PROGRESS.md` A1-7 row: "20 integration tests + 9 unit tests" (found during remediation) | Low | Actual A1-7 classes: 32 integration (Transfer 19 + Adjustment 9 + OpeningImport 4 — counted excluding `InitializeAsync`/`DisposeAsync`) + 20 unit (Transfer 9 + Adjustment 7 + OpeningImport 4); suite total 72 = 32 (A1-7) + 40 (A1-5/A1-6: posting 16 + WAC 24), matching README's "32/32 + 40/40" | Counts written mid-increment | Correct the counts | **CONFIRMED → fixed** |
| R-11 | A1-4P2…A1-7 status inconsistencies across HANDOFF/README/A1-PROGRESS | Medium | R-04, R-07, R-09, R-10 | Drift after `29d65ba` | All four corrected; single source = git | **CONFIRMED → fixed** |

## C. Master Data / Inventory incomplete deliverables

| ID | Finding (audit §) | Severity | Evidence | Root cause / decision | Remediation | Status |
|---|---|---|---|---|---|---|
| R-12 | MD-042 guard is a placeholder that allows everything (§4.3) | **Medium-High** | `DefaultLocationDeactivationGuard.cs` ("PLACEHOLDER … always allows"); `docs/A1-PROGRESS.md` D2 explicitly: "Inventory implements it in **A1-5**"; test `MD_042_StorageLocation_DeactivateWhileHoldingStock_PortPlaceholderAllows` | A1-5 shipped without replacing the registration — an incomplete deliverable of a *completed* increment with an already-approved design (D2) | Implement `LocationDeactivationGuard` in `Inventory.Infrastructure` (checks `StockBalance` for the location), wire it in the Host instead of the placeholder, add requirement-ID integration tests | **CONFIRMED → fixed in this session** |
| R-13 | A1-5 reconciliation job (INV-011 scheduled job) missing (§4.1, §11.2) | Medium | `grep -rn "Reconcil" src` → only transfer-shortfall adjustments; PRD `05-03` INV-011: "reconciliation job compares balances to movement sums" | Explicitly deferred by A1-5 §10 ("planned for later increment"); no increment owns it; schedule/alerting semantics are unspecified in the PRD | Register entry only: prerequisite = owner assigns the increment + decides mismatch handling (log vs audit vs alert) | **DEFERRED** (prerequisite: owner placement decision) |
| R-14 | A1-5 history queries (INV-013) missing (§4.1) | Medium | No query service in `src/Modules/Inventory`; PRD INV-013 (P1A): history by product/location/source doc/period/branch | A1-5 row claimed it; needs an endpoint + permission matrix → belongs to the first business API slice | Included in the first-API proposal (`API-INCREMENT-PROPOSAL.md`) | **DEFERRED → proposed** (prerequisite: permission matrix, R-24) |
| R-15 | A1-5 "opening-balance dry-run" (§4.1) | — | `OpeningImportOrchestrator` Validate/DryRun/Post; tests `INV_014_OpeningImport_DryRun_NoMovements`, `..._Validate_NonExistentWarehouse_Rejected` | Delivered in A1-7 | none | **ALREADY FIXED** (audit's own §4 table listed it under A1-5; it exists) |
| R-16 | Q-C `platform.User.IsCompanyWide` column never created (§4.4) | Medium | `BranchScopeProvider.cs:29` comment; no migration contains `IsCompanyWide`; HANDOFF §2: "Q-C migration file only, awaiting owner approval" — no file actually exists | Owner migration approval pending (Q-C) | none — migration must not be generated/applied before approval | **BLOCKED** (owner: Q-C migration approval) |
| R-17 | WAC-11 Inventory ADR not drafted (§4.11) | Low | `ADR/` has 001–006, 008, 009 | Requires owner acceptance of a new ADR; partially entangled with OD-03 (WAC-07) | none | **BLOCKED** (owner) |
| R-18 | PLT-073 has no business-document consumers (§4.12) | Low | `IDocumentSequenceService` used only by platform sequence allocation; transfers/adjustments carry no numbers | PRD PLT-073a example is `PO-…`; A1-7 requirements (INV-030/040/042/014) do not require document numbers | none — first consumers belong to A2 (PO/GR numbering) | **DEFERRED** (correct increment: A2; not an A1 defect) |
| R-19 | Remaining Inventory requirements: INV-002/003/004 buckets, INV-063/064/065 incoming, INV-041, WAC-07 (§5) | — | INV-004 partially covered (`INV_NEG_*`, DB CHECK); INV-081 has a passing test | Availability buckets + incoming projection are A1-8 scope; INV-041 blocked (ADR-007); WAC-07 blocked (OD-03) | none | **DEFERRED / BLOCKED** as marked — not A1-7 defects |

## D. Outbox and events

| ID | Finding (audit §) | Severity | Evidence | Remediation | Status |
|---|---|---|---|---|---|
| R-20 | Outbox is write-only: no dispatcher, publisher, consumer, retry/dead-letter (§4.6, §10 High) | High (for future slices) | `Outbox.EnqueueAsync` (transaction guard OK, OUT-01 ✓) + `InboxMessage` mark-* exist; **no** `BackgroundService` except `NotificationRetryWorker`; `OutboxMessage` has no `BranchId`/`CausationId`/retry-scheduling columns (HANDOFF §12 item 3) | Dispatcher ownership never assigned; OUT-02/03/04/06/07 and PLT-071 unimplemented | Proposal document `docs/audit-remediation/OUTBOX-DISPATCHER-PROPOSAL.md` (design, columns, retry/backoff/dead-letter, ownership options, migration impact). No speculative infrastructure built | **CONFIRMED → BLOCKED** (owner: dispatcher ownership + column migration approval) |

## E. Business API and permission matrices

| ID | Finding (audit §) | Severity | Evidence | Remediation | Status |
|---|---|---|---|---|---|
| R-21 | Zero Master Data/Inventory endpoints; only 10 platform endpoints (§4.7) | High (product-level) | `Endpoints/` = Auth, Notification, ApiKey | Correct — and correct *by design*: A1-2/A1-3 recorded "HTTP endpoints not done (no permission matrix, PLT-022)" | Proposal only, per authorization ("do not start implementing the entire business API") | **CONFIRMED → proposal produced** (`API-INCREMENT-PROPOSAL.md`) |
| R-22 | Only 4 permission codes; module permission matrix does not exist (§4.7, B-12) | High (blocks R-21) | `PermissionCodes.cs` (4 constants); PRD `06b` §6.4.1: "Full permission matrices produced at module design time (not optional)" | Matrices are design artifacts of each module slice; none produced yet | Draft matrices **as a proposal** for the first API increment (Master Data reads/writes + Inventory document commands), explicitly awaiting owner approval before any code seeds them | **CONFIRMED → proposal produced** (implementation BLOCKED until owner approves matrix) |

## F. Security and infrastructure (validated against HANDOFF §12 backlog, which already records these as "not approved work")

| ID | Finding (audit §) | Severity (validated) | Evidence / validation | Status |
|---|---|---|---|---|
| R-23 | No password policy | **Low** (audit implied High) | No PRD P1 requirement defines complexity/expiry/history (only PLT-010 "email+password", SEC-02 KDF via ADR). Recorded in HANDOFF §12 item 1 as needing an owner decision | **INCORRECT as a P1 defect / DEFERRED** — optional hardening, owner decision |
| R-24 | PBKDF2 100k iterations | Low | `PasswordHasher.Iterations = 100_000` — matches **accepted ADR-004**; changing it silently would change an accepted ADR (prohibited) and needs a rehash path | **BLOCKED** (owner: ADR-004 amendment if desired) |
| R-25 | Refresh-token rotation not atomic (no concurrency token) | **Medium-High** (real: two concurrent refreshes can both succeed) | `RefreshToken` entity has no `RowVersion`; `RefreshAsync` revoke-then-insert unguarded; HANDOFF §12 item 2; DATABASE-GUIDE §7 notes it | **BLOCKED** (owner: needs schema change/migration approval + auth behaviour decision; proposal recorded in final report) |
| R-26 | `Dockerfile` runs as root; app connects as `sa` | Medium (dev-context) | No `USER` in Dockerfile; `appsettings.json` sa placeholder guarded outside Development by SEC-20; HANDOFF §12 item 4 | **BLOCKED / DEFERRED** (owner: container hardening + DB principal change touches compose, CI, and an existing database) |
| R-27 | API-key auth is a manual header check, not a scheme | Low-Medium | `ApiKeyEndpoints` self-check endpoint only; ADR-009 froze the `X-Api-Key` header; HANDOFF §12 item 5 | **DEFERRED** (owner: convert to `AuthenticationHandler` when machine-facing endpoints exist) |
| R-28 | Committed dev `sa` password in `appsettings.json` | Low (already documented) | Placeholder identical to `.env.example`/CI; SEC-00…20 startup guard fails outside Development on the placeholder | **ALREADY MITIGATED** (documented placeholder + SEC-20); no change |
| R-29 | 14 leftover throwaway test databases on the SQL container | Low | `sqlcmd`: `COUNT(*) … LIKE 'SupplyForge_%'` = 14 | Dropping databases is prohibited by the safety rules without explicit authorization | **BLOCKED** (owner: authorize cleanup) |

## G. Test reliability

| ID | Finding (audit §) | Severity | Evidence | Remediation | Status |
|---|---|---|---|---|---|
| R-30 | Intermittent `Execution Timeout Expired` under parallel integration runs (§10 Medium, B-14) | Medium | HANDOFF §12 D0 measurement: 4 testhosts + 4 Hosts, SQL 110 % CPU, no `xunit.runner.json`; options A–E recorded, none applied | xUnit 2.9.3 defaults: up to 12 parallel classes per project, no cap | Implement **option B** (smallest, no coverage loss, no retries): `xunit.runner.json` with `parallelizeTestCollections: false` in the three integration test projects (content copy in each csproj). Unit/architecture projects untouched | **CONFIRMED → fixed in this session** (reliability re-verified by repeated full runs) |
| R-31 | PlatformCore integration tests share the shared dev database | Medium (design fact) | README §Testing notes it; they never CREATE/DROP it | Changing to throwaway DBs is a large redesign of the platform fixtures | **DEFERRED** (recorded as debt; unrelated test runs on one developer machine are the accepted trade-off today) |
| R-32 | Inventory tests excluded from solution made CI blind (duplicate of R-01) | — | see R-01 | — | **see R-01** |

## H. Other findings

| ID | Finding | Severity | Status |
|---|---|---|---|
| R-33 | 6 of 9 migrations unapplied; dev DB has no `master_data`/`inventory` schema (§4.8) | High for demo/E2E | **BLOCKED** — applying migrations to the existing dev database requires explicit owner authorization (never done in this session) |
| R-34 | MD-003 placement (Q7), Q-3 branch codes, Q-5 email-in-P1, Q-8 audit retention | Low | **BLOCKED** (owner questions) |
| R-35 | PLT-013 password-reset migration approval | Low | **BLOCKED** (owner) |
| R-36 | INV-041 / SEC-40 blocked on ADR-007 / ADR-011; WAC-07 on OD-03 | — | **BLOCKED** (owner) — unchanged, correctly not implemented |
| R-37 | Audit search API + before/after changed-field capture missing (§4.5) | Low-Medium | **DEFERRED** — PLT-041/043/045 are platform work outside this remediation; `AuditLog` + PLT-044 redaction exist; proposal noted in final report |
| R-38 | Pagination (PLT-076) missing | Low | **DEFERRED** — belongs to the first API slice (included in proposal) |
| R-39 | 189 `var` usages vs the recorded A1-5 "No `var`" rule (§4.10e) | Low | **DEFERRED** — a mass restyle risks churn for zero behaviour; owner must either rescind the rule or schedule enforcement |
| R-40 | No license file; `SF.png` missing | Low | **BLOCKED** (owner assets/decisions) |
| R-41 | No E2E/demo path (§10 Medium) | Medium (product) | **DEFERRED** — depends on R-33 (migrations) + A2…A4; not fixable by documentation |
| R-42 | NFR-060 compose `app` service absent; `docker-compose.yml` has only sqlserver | Low-Medium | **DEFERRED** (B4 scope; also depends on container-hardening decision R-26) |
| R-43 | Audit's weighted completion ≈ 21 % (§6) | — | **ACCEPTED** — arithmetic re-checked (21.43); updated estimate in the final report after remediation |

---

## Summary counts

| Status | Count |
|---|---|
| CONFIRMED → fixed in this session | 11 (R-01, R-02, R-04…R-12, R-30) |
| ALREADY FIXED / ALREADY MITIGATED | 2 (R-15, R-28) |
| PARTIALLY FIXED | 1 (R-03) |
| INCORRECT / overstated as stated | 1 (R-23) |
| BLOCKED — owner decision/authorization | 14 (R-16, R-17, R-20, R-22, R-24, R-25, R-26, R-29, R-33…R-36, R-40) |
| DEFERRED — valid, out of authorized scope | 11 (R-13, R-14, R-18, R-19, R-21, R-27, R-31, R-37…R-39, R-41, R-42) |
| Proposal documents produced | R-20 (outbox), R-21/R-22 (first API increment) |

No finding was marked fixed without an executed verification command; the exact
commands and results are in `FINAL-REMEDIATION-REPORT.md` §5.
