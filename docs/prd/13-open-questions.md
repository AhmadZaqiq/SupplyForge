<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §13 · nothing changed · navigation: docs/prd/INDEX.md -->

## 13. Open questions

### 13.1 Product-owner questions

Status key: **Resolved** = settled from existing requirements/ADRs · **Default** = conservative default in force, override possible until stated deadline · **Open-PO** = product-owner answer still required at the listed stage.

| ID | Question | Impact | Status |
| --- | --- | --- | --- |
| Q-1 | Demo base currency/locale (USD A-1)? | Money scale | **Default:** USD scale 2 in force (ADR-014/TD-1); **locale confirmation Open-PO before B2** (non-blocking) |
| Q-2 | Country tax/legal / tax-inclusive? | Config only; **no compliance claimed** | **Open-PO** — before any real-world deployment; not P1-blocking (config rates only) |
| Q-3 | Three branch names/codes + warehouses seed? | Seed | **Open-PO** — before demo seed; placeholder codes/names acceptable until then (non-blocking) |
| Q-4 | Agreements & customer returns stay P2? | Scope | **Resolved** — stay P2 per §2.5 scope (agreements P2, customer returns P2) |
| Q-5 | Email in P1 vs manual-sent? | Notifications | **Default:** manual mark-sent OK (TD-2/ADR-008); **Open-PO confirm before B4** |
| Q-6 | Credit/overdue thresholds? | Config | **Open-PO** — seed values before A3 demo; SAL-060 already treats them as configuration (non-blocking for code) |
| Q-7 | Lot/serial early? | D-11 | **Resolved** — P2 per D-11/INV-070 |
| Q-8 | Audit retention? | SEC-31 | **Open-PO** — duration config before B4; default retention window applies until set (non-blocking) |
| Q-9 | Approval default amounts? | Seeds | **Default:** two-level seeds per D-10/ADR-007; **Open-PO** threshold numbers before demo seed (non-blocking) |
| Q-10 | Thin demo UI required? | D-1 | **Default:** API-first, no UI for gates (D-1); **Open-PO** before B4 if a demo UI is wanted |
| Q-11 | Future: allow multi-scope users to allocate cross-branch **without** `finance.payment.cross_branch`? (Current P1B = cross_branch required, TD-8/FIN-047 — Q-11 is relaxation only.) | FIN-050b | **Open (future)** — explicitly non-blocking; current behavior unambiguous (FIN-047/050b) |
| Q-12 | WAC true-up on supplier credit variance? (TD-4) | WAC-06 | **Default:** no true-up (TD-4); **Open-PO before B2** |
| Q-13 | Allow Adjustment reason to post into Returns in P1? (TD-7) | INV-091 | **Default:** strict guard, no path (TD-7); **Open-PO before B3** |
| Q-14 | **Dispatch-then-cancel billing policy:** if a dispatch commits and the SO is later cancelled before invoice generation, invoice anyway or allow Skipped? | FIN-029a, SAL-052 | **Resolved for P1 (ADR-013):** dispatch remains billable; no auto-skip; correction via credit note/reversal. **PO override window: until B1 start** (after that, new ADR required) |

---

### 13.2 Change control and approval

1. Requirement changes update ID, priority/stage, change log.  
2. P2→P1A/P1B promotion needs explicit PO approval + trade-off.  
3. Architecture choices → ADRs in `ADR/` (NFR-043 list; stage-gated ADRs tracked in `docs/Decision Register.md`).  
4. Approval = accepts open questions + temporary defaults process — **not** production readiness.

---
