# SupplyForge — Decision Register

**Purpose:** Track PA/PC items, ADR acceptance status, owner decisions (OD-n), and open questions (Q-n) with their defaults. Authority: PRD `00-how-to-use.md` rules 1–3, 7; `13` §13.2.  
**Created:** 2026-11-16 (file was missing; referenced by PRD §12 and `01` §1.3).  
**Status key:** `Accepted` · `Proposed (pending PO)` · `Open-PO` · `Default in force` · `Resolved` · `Superseded` · `Blocked`

---

## 1. M0 / A0 ADRs (this delivery)

| ID | Topic | PRD says | File status | Acceptance |
|---|---|---|---|---|
| ADR-001 | Solution layout + leaf contract assemblies | **DONE** in NFR-043(3) | Accepted 2026-11-16 | **Accepted** |
| ADR-002 | Event versioning + consumer-owned DTOs | **DONE** in NFR-043(4) | Accepted 2026-11-16 | **Accepted** |
| ADR-003 | Ambient TXN, outbox atomicity, DbContext composition | **DONE** in NFR-043(5) | Accepted 2026-11-16 | **Accepted** |
| ADR-004 | Identity: KDF, tokens, lockout, scope freshness | **Not named in PRD**; topic inferred from unassigned “(ADR)” pointers (PLT-010, SEC-02, PLT-ROLE-5) | Accepted 2026-11-16 | **Accepted — topic = Identity** |
| ADR-005 | Document numbering / sequences | **DONE** in NFR-043(9) | Accepted 2026-11-16 | **Accepted** |
| ADR-006 | API surface (version/routes/paging/errors/concurrency/idempotency/auth HTTP) | **Decided** NFR-044/045 | Accepted 2026-11-16 | **Accepted — `X-Api-Key` frozen** |
| ADR-007 | Approval engine | Required A1 (`11`) | **Not drafted** (out of this batch) | Not started |
| ADR-008 | Notifications + email fake | Required A0 (`11`, PLT-050…052) | Accepted 2026-11-16 | **Accepted** |
| ADR-009 | API keys with scopes | Required A0 (`11`, INT-050) | Accepted 2026-11-16 | **Accepted — `X-Api-Key` frozen** |
| ADR-010 | (Not specified in PRD reads) | Unknown | — | — |
| ADR-011 | Logo scope narrow + SEC-50/INT-005 → P2 justification | Referenced `11` | **Not drafted** | A1/P2 |
| ADR-013 | Dispatch remains billable (dispatch-then-cancel) | Resolved Q-14 | **Not drafted** | Before B1 |
| ADR-014 | Currency / USD scale 2 | **DONE** NFR-043(11) | **Not drafted** (file missing) | Needed before money code |
| ADR-015 | PO cancel/revised → inbound | Referenced INB-004/005 | **Not drafted** | A2 |
| ADR-016 | PRC-055 invoiced counters activate B2 | Referenced `11` | **Not drafted** | A2/B2 |
| ADR-017…019 | Proposals only | `00` rule 1 | Not accepted decisions | Stop-and-ask |
| ADR-020 | (Proposed) GR reduces incoming — OD-07 | `05-03` INV-065 | Proposal only | Stop-and-ask |

**Note:** PRD marks 001–006 core as required before projects (`AGENTS.md`, HANDOFF). **PO accepted ADR-001…006, 008, 009 on 2026-11-16** (including recommendation defaults; ADR-004 topic = Identity; API key header = `X-Api-Key`). Solution/projects may be created.

---

## 2. Items awaiting owner approval (OD-n) — do not implement behaviour

| ID | Topic | Requirement | Stage |
|---|---|---|---|
| OD-02 | `PurchaseOrderClosed` partial-close event | PRC-054, INB-006, INV-064; ADR-017 | A2+ / stop-and-ask |
| OD-03 | WAC late-reversal eligibility | WAC-07; TD-5; ADR-018 | **Before A1 costing** |
| OD-04 | Line discount approval details | SAL-022 | A3+ |
| OD-05 | Credit override contract | SAL-061; MOD-12 note; ADR-019 | A3+ |
| OD-07 | GR decrements incoming same TXN | INV-065; ADR-020 | A2+ |

---

## 3. Open questions (Q-n) — defaults in force where stated

| ID | Status | Default / note | Decide by |
|---|---|---|---|
| Q-1 | Default in force | USD scale 2 (TD-1, ADR-014); locale Open-PO | Before B2 |
| Q-2 | Open-PO | Tax/legal config only; not P1-blocking | Before real deployment |
| Q-3 | Open-PO | Placeholder branch names OK until seed | Before demo seed |
| Q-4 | Resolved | Agreements & customer returns stay P2 | — |
| Q-5 | Default in force | TD-2 manual mark-sent / email optional (**ADR-008**) | Before B4 |
| Q-6 | Open-PO | Credit thresholds config; non-blocking for code | Before A3 demo |
| Q-7 | Resolved | Lots P2 | — |
| Q-8 | Open-PO | Audit retention duration; use configurable default | Before B4 |
| Q-9 | Default + Open-PO | Two-level approval seeds (ADR-007); threshold numbers later | Before demo seed |
| Q-10 | Default in force | API-first, no UI for gates (D-1) | Before B4 if UI wanted |
| Q-11 | Open (future) | Cross-branch relax; P1B uses TD-8 `finance.payment.cross_branch` | Non-blocking |
| Q-12 | Default in force | No WAC true-up (TD-4) | Before B2 |
| Q-13 | Default in force | No Adjustment→Returns in P1 (TD-7) | Before B3 |
| Q-14 | Resolved for P1 | Dispatch stays billable (ADR-013) | PO override until B1 start |

---

## 4. Temporary defaults (TD-n) relevant near-term

| ID | Default | Owner | Decide by |
|---|---|---|---|
| TD-1 | USD scale 2 | PO | Before B2 |
| TD-2 | Email optional; manual mark-sent OK | PO | Before B4 |
| TD-3 | Credit check fails closed | Architect | A3 design review |
| TD-5 | WAC scale 8; reversal pending OD-03 | Architect+PO | Before A1 costing |
| TD-6 | A5 money stub **SKIP** | PO | Start of B1 |
| TD-10 | Shipment TXN2 idempotent by DispatchId (ADR-003) | Architect | Test before A4 |

---

## 5. PA / PC items

| ID | Item | Status |
|---|---|---|
| PA-17 | A5 money stub optional; default skip | In force (TD-6) |
| PA-18 | **DM-3 amendment, owner-accepted 2026-11-24:** Guid surrogate keys are the project key style (not BIGINT). Accepts option (a) of `A1-PROGRESS.md` §0 B4 — the default already in force since A1-1 (`Company.Id`, `Branch.Id`, all A0 platform ids). DM-3's BIGINT text stands superseded for surrogate keys; no conversion scheduled. | **Accepted** (A1-3 answer D2) |
| — | Other PA/PC | None recorded yet |

---

## 6. Missing inputs still blocking implementation

| Input | Impact |
|---|---|
| ~~PO acceptance of ADR-001…006, 008, 009~~ | **Done 2026-11-16** — M0 unblocked |
| ~~ADR-004 topic confirmation~~ | **Done — Identity** |
| ADR-007, 011, 013, 014, 015, 016 | Later slices / money |
| Env: SQL Server 2022 via Compose approved; migrations on **local Docker DB only** allowed | M0-2 / M0-3 |
| ~~Baseline git commit~~ | **Resolved 2026-11-18** — commit/push allowed only per explicit owner approval (aligns with HANDOFF and AGENTS) |

---

## 7. Change log

| Date | Change |
|---|---|
| 2026-11-16 | Register created; ADR-001…006, 008, 009 drafted as Proposed pending PO review |
| 2026-11-16 | PO accepted ADR-001…006, 008, 009 + defaults; ADR-004 topic = Identity; `X-Api-Key` frozen; M0-0…M0-5 approved; local-only EF migrations approved; no baseline commit |
| 2026-11-24 | **PA-18 added:** owner accepted Guid surrogate keys as the project key style — DM-3 amendment (A1-3 answer D2). A1-3 design decisions D1–D17 and Q1–Q7 recorded in `docs/A1-PROGRESS.md` §1b |

---

## 8. A1-4 owner decisions (2026-11-24)

### Accepted

| ID | Decision |
|---|---|
| Q-A | `Warehouse.Code` unique company-wide, **non-filtered**. |
| Q-B | Entity/table name **`StorageLocation`** (DM-9.2). |
| Q-C | `IsCompanyWide` on `platform.User` in **Part 2**, migration **file only**. BRN-010: "Users assigned one+ branches or company-wide scope flag." |
| Q-D | `ICurrentBranchScope` = `BranchIds` + `IsCompanyWide` + `CanAccess(Guid)`. |
| D1 | **One** branch-scoping mechanism; every future stock table carries `WarehouseId` directly. |
| D2 | MasterData owns `ILocationDeactivationGuard` in `MasterData.Contracts`; Inventory implements it in **A1-5**; A1-4 ships a public placeholder that allows everything. |
| D3 | `CreatedAt`/`UpdatedAt` are `datetimeoffset`, set from `TimeProvider`, **no SQL default**. |
| D4 | **No delete operation**; `Restrict` only blocks deleting a parent with children; cross-module references are plain `Guid` with **no FK** (DM-4). |
| D5 | `Warehouse.Code` unique company-wide, non-filtered. |
| D6 | Branch scope must **not** use `AmbientDbSession`; `ICurrentBranchScope` on the Platform Core side; EF global filter reads a **DbContext property**; out-of-scope create rejected; company-wide sees all; a user with no branches sees nothing. **Part 2 only.** |
| — | **Removed** `CK_StorageLocation_IsBlocked_Implies_Inactive`: not PRD-required; a blocked Storage location can stay active. |

### Open

| ID | Requirement |
|---|---|
| Q-E | Null branch scope must **fail closed for normal requests**; **seed and migration operations must retain the required system-level data access**. No resolution chosen. |

## 9. Workflow authority

- Owner decisions recorded here override agent assumptions and old plans.
- A superseded plan must be marked `SUPERSEDED` with a link to its replacement.
- No commit, push, or migration application without separate explicit owner approval.
- If HANDOFF, Git, actual source, or this register disagree, stop and report; never resolve with assumptions.
- If the previous agent's inactivity cannot be verified, do not write until the owner confirms the handoff.
