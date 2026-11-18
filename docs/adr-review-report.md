# Review Report — ADR-001…006, 008, 009 (A0 M0 set)

## Overview

- **Scope reviewed:** `ADR/ADR-001.md` … `ADR-006.md`, `ADR/ADR-008.md`, `ADR/ADR-009.md`, `docs/Decision Register.md` against PRD v0.8 parts already read for A0 + Core set, project `AGENTS.md`, `docs/HANDOFF.md`, library rules/expertise used for defaults.
- **Not reviewed:** PRD archive single-file copy (except greps already done); ADR-007/011/013–016 content (not drafted); application code (none exists); runtime behaviour (nothing implemented).
- **Type:** Documentation / architecture decision review (not code).

## Summary

Eight ADRs were **reconstructed** from PRD-stated outcomes plus clearly marked recommendations where the PRD left parameters open. PRD-fixed decisions (leaf contracts, `/api/v1`, gaps in numbering, outbox same-TXN, TD-2 email fake, INT-050 scopes) were not re-opened. **ADR-004’s number/topic is not named anywhere in the PRD**—identity was assigned from unassigned “(ADR)” pointers. **Update 2026-11-16: the owner accepted all eight ADRs, including the ADR-004 topic = identity (see `docs/Decision Register.md`).** No contradictions were found with MOD-1…10, §4.5, NFR-044/045, or TD-2/TD-10. Solution/projects were **not** created at review time; nothing was committed.

---

## Per-ADR decisions and trade-offs

### ADR-001 — Solution layout + leaf contract assemblies
| | |
|---|---|
| **Confirmed (PRD)** | One deployable modular monolith; C/R/E kinds; leaf contracts; **no shared contracts assembly** (DM-8); acyclic graphs; MOD-1/1a shared-table list; NFR-040 tests both graphs; MOD-8 stack bans |
| **Recommendation (your approval)** | Folder/project naming (`src/Modules/...`, `SupplyForge.*.Contracts`), single `.sln`, test project naming |
| **Trade-off** | More csproj files vs enforceable module boundaries; rejects classic layer-sliced Clean Architecture because PRD module rules win (project AGENTS) |
| **Risk** | Layout bikeshed—freeze names once |

### ADR-002 — Event versioning + consumer-owned DTOs
| | |
|---|---|
| **Confirmed (PRD)** | Envelope OUT-04; version OUT-05 additive/breaking rules; consumers never reference publisher assemblies; JSON implied by outbox storage + .NET stack; inbox dedupe; no broker |
| **Recommendation** | `eventType` string style; integer `version`; camelCase; unknown-property tolerance test |
| **Trade-off** | No compile-time event typing (safety) vs duplicated consumer DTOs (cost) |
| **Risk** | Silent breaking edits—mitigated by version bump + tests |

### ADR-003 — Ambient TXN + outbox + DbContext
| | |
|---|---|
| **Confirmed (PRD)** | Only R-in-one-TXN and E-after-commit; orchestrator = owning module; no network in TXN; outbox same TXN; MOD-1a interfaces only; MOD-TXN-05 fail whole TXN; IDEM retention 72h / dispatch-invoice ≥7d; TD-10 two-TXN dispatch; NFR-050 real SQL tests |
| **Recommendation** | Per-module `DbContext` + shared `DbTransaction` join; `IDbSession`-style composition (not MediatR); `READ COMMITTED` |
| **Trade-off** | Ownership clarity + testability vs non-trivial transaction composition |
| **Risk** | High-severity bugs if contexts open a second connection—call out in tests |

### ADR-004 — Identity (KDF, tokens, lockout, scope freshness) ⚠ topic not PRD-named
| | |
|---|---|
| **Confirmed (PRD)** | Admin-created users; strong KDF; access+refresh rotation/reuse detection; lockout/rate limit; password reset single-use; permission catalogue + branch scope; deny-default; re-authz every request; BRN 401/403/044; no IdP/2FA in P1 |
| **Recommendation** | Identity PBKDF2; access JWT ~15m; refresh opaque hashed 14d + family revoke; lockout 5/5min; scope cache TTL 5min **or** refresh (PLT-ROLE-5); HTTPS prod |
| **Trade-off** | Stateless access JWT speed vs not-instant access revoke—accepted via short TTL + PLT-ROLE-5 |
| **Risk** | Wrong ADR number/topic if you intended ADR-004 for something else—**confirm before acceptance** |

### ADR-005 — Document numbering
| | |
|---|---|
| **Confirmed (PRD)** | Per-type patterns; optional branch/fiscal (D-15); DB unique + concurrent-safe gen; **gaps allowed**; voided numbers not reused; `DocumentSequence` via MOD-1a interface; display ≠ surrogate key |
| **Recommendation** | Atomic counter UPDATE; uniqueness key shape; zero-pad seq; assign number at draft creation |
| **Trade-off** | Simple gap-prone counters vs illegal gapless complexity PRD forbids without legal need |
| **Risk** | Hot sequence row—acceptable at P1 scale (G-8) |

### ADR-006 — API surface
| | |
|---|---|
| **Confirmed (PRD)** | **`/api/v1`** additive; breaking → `/api/v2` (NFR-044); NFR-045 full surface; ProblemDetails + machine code + correlation (PLT-074); pagination (PLT-076); optimistic concurrency (PLT-072/DM-7); Idempotency-Key semantics (IDEM-02…05); Bearer for users; BRN status matrix; INT-052 OpenAPI |
| **Recommendation** | Error code style; correlation header name; page size 20/max 100; concurrency **409**; idempotency mismatch **409**; v1 support ≥90d after v2; single API-key header scheme |
| **Trade-off** | URL version permanence vs explicit compatibility—already decided by PRD |
| **Risk** | Dual auth schemes if ADR-009 header not aligned—cross-checked below |

### ADR-008 — Notifications + email fake
| | |
|---|---|
| **Confirmed (PRD)** | PLT-050…052; INT-001/002/003/030; TD-2/Q-5 manual mark-sent; no network in TXN; failures visible; fake-first (D-6) |
| **Recommendation** | `Email:Mode` Fake/Smtp/Disabled default Fake; closed notification type list; fixed templates; round-trip test definition |
| **Trade-off** | No real SMTP in A0 vs honest queue/retry semantics without provider |
| **Risk** | Fake diverging from real sender—share one `IEmailSender` contract |

### ADR-009 — API keys + scopes
| | |
|---|---|
| **Confirmed (PRD)** | INT-050 scopes + revoke; Integration Client least privilege; permission catalogue; deny-default; BRN still applies; secrets never in repo/logs; deactivate not hard-delete; A0 scope test |
| **Recommendation** | Opaque key + hashed secret; permission allow-list on key; optional expiry; **one** header scheme with ADR-006 |
| **Trade-off** | Simple opaque keys vs full OAuth client credentials (not required in P1) |
| **Risk** | Header mismatch with ADR-006—flagged as joint decision |

---

## Cross-check: contradictions and consistency

| Check | Result |
|---|---|
| ADR-001 leaf contracts vs library Clean Architecture layers | **Consistent with override:** project AGENTS says PRD modular monolith wins (library rule 4). ADR-001 states this explicitly |
| ADR-001 no shared contracts vs DM-8 `Finance.Contracts` | **Consistent** — per-module leaf, not a global Contracts project |
| ADR-003 no distributed TXN vs MOD-TXN-01 | **Consistent** |
| ADR-003 outbox same TXN vs OUT-01/MOD-3 | **Consistent** |
| ADR-003 IDEM-06 72h / ≥7d vs PRD | **Verbatim consistent** |
| ADR-003 TD-10 / FUL-032 / SHP-010 | **Consistent** with design notes |
| ADR-002 consumer DTOs vs §4.1 E | **Consistent** |
| ADR-004 refresh rotation vs PLT-011/SEC-03 | **Consistent** |
| ADR-004 scope 5 min vs PLT-ROLE-5 | **Consistent** (implements stated default) |
| ADR-005 gaps allowed vs PLT-073c | **Consistent** |
| ADR-006 `/api/v1` vs NFR-044 | **Consistent** (PRD-fixed) |
| ADR-006 401/403/044 vs BRN-011…014 / SEC-13 | **Consistent** — defers to BRN policy |
| ADR-008 TD-2/Q-5 vs PLT-051 | **Consistent** — config-optional email, pending visible |
| ADR-009 scopes vs PLT-020/INT-050 | **Consistent** |
| ADR-006 auth header vs ADR-009 | **Aligned by design:** both say “pick **one** API-key scheme”; must be frozen together at approval |
| ADR-004 vs ADR-006 split | **Clear:** 004 = identity internals; 006 = HTTP presentation of auth/errors/versioning |
| MOD-8 (no MediatR/Redis/broker) | All ADRs avoid introducing them; ADR-003 explicitly rejects MediatR pipeline recommendation |
| ADR-017…020 / OD-n | **Not implemented**; Decision Register lists them as stop-and-ask |
| Invented “accepted” history | Avoided at draft time: files were marked **Proposed — reconstructed; pending PO**; the owner then accepted them on 2026-11-16 (headers now say Accepted) |

### Gaps / missing details (honest limits)

| Gap | Severity | Handling |
|---|---|---|
| PRD never names **ADR-004** topic | ~~High for acceptance~~ **Resolved** | Owner confirmed topic = identity, 2026-11-16 (Decision Register) |
| PRD never names **ADR-010**, **ADR-012** | Info | Not in A0 batch; register notes unknown |
| Original accepted ADR text unavailable | Medium | Reconstruction may differ in wording from owner’s lost files—diff if owner has copies |
| Exact KDF iterations, JWT TTL, page sizes, API-key header | Medium | Marked **Recommendation** tables—approve or edit |
| ADR-014 currency file still missing | Medium for money slices | Listed in register; not A0-blocking |
| Env (SQL Server) still not running | High for NFR-050 tests later | Out of scope this batch (no containers started) |

---

## Counts

- **Critical:** 0  
- **High:** 0 (was 1 — ADR-004 topic confirmation; **resolved 2026-11-16**)  
- **Medium:** 3 (recommendation parameters unfrozen; ADR-006/009 header joint choice; missing later ADR files 007/011/013–016)  
- **Low:** 2 (eventType style; error-code catalogue growth)  
- **Info:** reconstructed-not-original status on all eight files  

## Positive Observations

- Every PRD “DONE: ADR-00n” pointer now has a file that answers NFR-043’s required questions for ADR-003 explicitly.
- Recommendations are isolated in tables so PRD-confirmed lines can be accepted without re-deciding product policy.
- Decision Register captures OD/Q/TD so later slices do not re-discover stop-and-ask items.
- No package installs, containers, solution, code, or git commits were performed.

## Verification Performed

- Read: PRD `00`, `01`, `02a`, `02b`, `03`, `04a`, `04b`, `04c`, `05-01`, `06a`, `06b`, `06d`, `07`, `08`, `09`, `10`, `11`, `12`, `13`, `INDEX`, `HANDOFF`, project/library `AGENTS.md`, library `decision-record`/`review-report` templates, `API_SECURITY.md`, `WEB_API.md`, `CLEAN_ARCHITECTURE.md`, `principles.md`.
- Grep: `ADR-00[1-9]`, `ADR-01[0-6]`, `NEEDS OWNER APPROVAL`, `OD-0`, `Open-PO` across repo (excluding reliance on archive as authority).
- Disk: `ADR/` was empty before draft; no `Decision Register.md` before draft; no `.sln`/`src`; git still no commits.
- **Not run:** build, tests, format (no code). **Not done:** commit/push, package install, container start.

## Limitations

- Cannot verify against the owner’s original lost ADR documents.
- Cannot execute NFR-050 tests until env + solution exist.
- ~~ADR-004 topic is an inference.~~ Resolved: owner confirmed identity, 2026-11-16.

## Verdict

**Update 2026-11-16: the owner accepted ADR-001…006, 008, and 009** (PRD-confirmed sections + Recommendation tables), **confirmed ADR-004 topic = identity**, and froze the ADR-006/009 `X-Api-Key` header scheme — recorded in `docs/Decision Register.md`. Solution creation was approved; M0 and slice A0 followed.

Original review request (superseded by the acceptance above):

1. **Accept or amend** each of ADR-001…006, 008, 009 (PRD-confirmed sections + Recommendation tables).  
2. **Confirm ADR-004 topic** = identity (or rename/replace).  
3. **Freeze jointly:** ADR-006 + ADR-009 API-key header scheme; error idempotency/concurrency codes (409).  
4. Then only: approve solution creation per accepted ADR-001 (still separate from this documentation batch).

**No application code, projects, packages, containers, or git commits were created by this review.**
