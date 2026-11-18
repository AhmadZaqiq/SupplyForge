<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §11 · nothing changed · navigation: docs/prd/INDEX.md -->

## 11. Delivery plan

### 11.1 Delivery rationale

Deliver a **working end-to-end business workflow early** (P1A), then complete money depth and hardening (P1B), without silently promoting P2/P3.

### 11.2 Slices and gates

**P1A — “Operate the warehouse loop”**

| Slice | Contents | Acceptance gate |
| --- | --- | --- |
| A0 Foundations | Repo, CI, Docker, arch tests, M0 ADRs (ADR-001…006 core), Platform auth/branch/audit/outbox/idempotency/sequences **+ notifications minimal (PLT-050…052 — ADR-008) + email adapter fake (INT-030) + API keys (INT-050 — ADR-009)** | Sign-in; branch isolation tests; notification round-trip; API-key scope test; CI green |
| A1 Master + Inventory core | Company singleton, 3-branch seed, products/partners/warehouses/locations, ledger, balances, adjustments, transfers, opening, WAC core, **approval engine (PLT-030…034 — ADR-007, seeds per D-10/Q-9)**, **logo upload (SEC-40 narrowed scope — ADR-011)** | Reconciliation; INV-004 tests; WAC-02/03/08 tests; INV-041 via approval engine; logo upload tests |
| A2 Buy & receive | Req, approval, RFQ/compare, PO, expected delivery, GR, **partial inspection**, putaway, supplier return doc | PO→available both paths; inspection invariants; short/damage |
| A3 Sell & reserve | SO, pricing, availability, **credit reservation contract**, reserve, backorder, cancel | No over-reserve; **atomic credit tests**; cancel releases reservation **and credit**; MOD-9 arch test |
| A4 Fulfil & dispatch | Pick, pack, **dispatch stock**, shipment row, manual tracking, delivery confirm | Dispatch commits stock; D-4; FUL-036 |
| A5 Money stub (optional) | Manual customer invoice **only if** needed for demo before B — else skip (TD-6). **Default: SKIP** (re-add only if a pre-B demo requires it — PA-17) | If present: manual invoice ≤ dispatched qty |

**P1B — “Close the loop with money and insight”**

| Slice | Contents | Acceptance gate |
| --- | --- | --- |
| B1 Invoice generation | Async per-dispatch `InvoiceGeneration` row, statuses incl. **Skipped (FIN-029a)**, retry on same row, reconcile, monitoring; **dispatch vs invoice status separation (FUL-036…038)** | FIN-027…039, FIN-029a, FUL-036…038; unique DispatchId; ≤1 invoice; failure isolation; skipped-state tests; **billing policy per ADR-013 (dispatch stays billable; PO override window closed at B1 start)** |
| B2 AP/AR depth | Supplier invoice record + three-way match, supplier/customer payments, allocations, credit notes, exposure components, **cross-branch payment rules** | Match tests; allocation/credit caps (FIN-CALC-13…16); FIN-001/002/007, FIN-010…014, FIN-045…052 |
| B3 Thin insight | Minimal dashboard, stock/open docs reports, gen-failure widget | Branch-scoped reports; NFR-025 W5 |
| B4 Hardening | Perf W1–W5 samples, security pass, docs, demo data, write-up | NFR spot-checks; one-command run; traceability |

**Still not P1:** AI, full analytics, real payment/carrier sandboxes, customer returns workflow, lots, stock counts, PDF invoices (P2), supplier agreements (P2).

### 11.3 Priority-to-gate mapping

| Priority tag | Meaning |
| --- | --- |
| P1 (unspecified) | P1A if foundation/master/inventory/procure/inbound/sales/fulfillment; **P1B** if finance depth (match, payments UX, AR gen, aging), thin RPT, hardening NFRs |
| P1A | Must be in A0–A4 gates **or carry a documented waiver/retag (ADR-011)** |
| P1B | Must be in B1–B4 gates |
| P2 / P3 | Unchanged; **no silent promotion** |

Explicit P1B list (non-exhaustive): FIN-001, FIN-002, FIN-003, FIN-007, FIN-010…014, FIN-020…020b, FIN-027…029, FIN-029a, FIN-036…039, FIN-040, FIN-045…049, FIN-050b, FIN-052, **FIN-CALC-13…16** (credit-note and payment-allocation caps — consumers are P1B), FUL-036…038, RPT-001…004/010/020 thin, NFR-011, NFR-025, most §10 money tests. **No P1A-tagged Finance requirement lacks an A0–A5 slice** (remaining Finance P1A: FIN-041 credit contract → A3; FIN-050 tax calc → A3 pricing; FIN-CALC-01…12 general money rules → A3).

**P1A homes:** INT-050 API keys → **A0** (ADR-009); PLT-050…052 notifications + INT-030 email adapter fake → **A0** (ADR-008); PLT-030…034 approval engine → **A1** (ADR-007); SEC-40 → **A1, narrowed to logo scope**; INV-063 / INB-004 / INB-005 → **A2** (ADR-015); PRC-055 invoiced counter values activate at **B2** (mechanism ADR-016; columns exist from A2). **Out-of-P1 requirements with documented justification:** SEC-50, INT-005 → **P2** (ADR-011 — no P1 inbound webhook source). The traceability matrix is generated from requirement IDs and their gate homes.

### 11.4 Priority policy

Untagged requirements invalid. P2→P1 promotion needs product-owner approval and a trade-off.

---
