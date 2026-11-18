<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.3 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.3 Inventory (INV)

**Quantities (no double-count)**

- **INV-001 (P1A)** Stock tracked per product, warehouse, location (and lot/serial when P2 tracking enabled).
- **INV-002 (P1A)** Quantity terms (authoritative):

| Term | Definition |
| --- | --- |
| **On-hand** | Physical units in warehouse locations (any P1 location type). Never negative. |
| **Reserved (unpicked)** | Units committed to active sales-order demand that remain in eligible Storage. Only this quantity is subtracted from eligible_on_hand. |
| **Blocked** | (a) all units in **Quarantine**; (b) Storage units on explicit hold; (c) Returns pending disposition. Subset of on-hand by location/flag — **not** an extra copy of quarantine. |
| **Picked-reserved** | Units already moved from Storage to Staging by picking but not yet dispatched or released. Report separately; do not subtract them from eligible_on_hand because Staging is excluded. |
| **Available** | `eligible_on_hand − unpicked_reserved − blocked_in_eligible`, where **eligible_on_hand** = on-hand in **Storage** not blocked. **Staging, Receiving, Quarantine, Returns are not eligible for new reservations.** |
| **In-transit** | Approved transfers not yet received at destination. **Not** either warehouse’s on-hand. |
| **Incoming** | Open PO qty not yet received (planning signal; **not** a stock bucket). |

  **Double-count rule:** Quarantine/Returns/Receiving/Staging are excluded from **eligible_on_hand** and are **not** subtracted again as a separate blocked line. Blocked-in-Storage subtracted once. A unit under an active reservation cannot satisfy another reservation.
- **INV-003 (P1A)** Receiving, Quarantine, Returns, Staging: on-hand but **not available** for new reservations.
- **INV-004 (P1A)** **Invariant:** `on_hand ≥ 0`; `available ≥ 0`; `unpicked_reserved ≤ eligible_on_hand`; same physical unit never covered by two active reservations.

**Ledger & projection**

- **INV-010 (P1A)** Inventory shall record immutable StockMovement entries for every authorized stock movement.
- **INV-011 (P1A)** StockBalance updated in the **same DB transaction** as movements; reconciliation job compares balances to movement sums; no ad-hoc balance edits.
- **INV-012 (P1A)** Reversals point to original; movements never edited/deleted.
- **INV-013 (P1A)** History by product, location, lot, source document, period, branch.
- **INV-014 (P1A)** Opening balances import with dry-run → OpeningBalance movements.

**Reservations**

- **INV-020 (P1A)** Per SO line: qty, warehouse, optional location/lot; only from **available**.
- **INV-021 (P1A)** Concurrent reservation atomic: no over-commit; no double-reserve. **PRD = invariants + acceptance tests only**; SQL Server strategy chosen in Inventory ADR (D-14; consider lock escalation and blocking trade-offs — no mandated hints in PRD).
- **INV-022 (P1A)** Serve in order of SO confirmation time.
- **INV-023 (P1A)** Partial reservation OK; remainder → backorder to Sales.
- **INV-024 (P1A)** Reservation states shall distinguish unpicked, partially picked, picked, partially consumed, consumed, released, and expired quantities.

**Design note (INV-024/026):** Picking transfers quantity from unpicked-reserved to picked-reserved in the same transaction as Storage → Staging. Picked-reserved quantity remains linked to the reservation until dispatch or release.
- **INV-025 (P1A)** Release on cancel/reduce.
- **INV-026 (P1A)** Consume picked-reserved quantity on dispatch (issue from Staging against the reservation); dispatch reduces both Staging on-hand and picked-reserved quantity in the same TXN.
- **INV-027 (P2)** Expiry; multi-warehouse split lines.


**Worked reservation example (all quantities in base units; no lot split):**

| Step | Storage on-hand / eligible | Staging on-hand | Unpicked reserved | Picked-reserved | Available | Result |
|---|---:|---:|---:|---:|---:|---|
| Initial | 5 / 5 | 0 | 0 | 0 | 5 | Five units eligible |
| Reserve 5 | 5 / 5 | 0 | 5 | 0 | 0 | Reservation Active |
| Pick 5 | 0 / 0 | 5 | 0 | 5 | 0 | Reservation Picked; unpicked reservation moves out of Storage with the stock |
| Dispatch 5 | 0 / 0 | 0 | 0 | 0 | 0 | Reservation Consumed; Issue movement posted |
| Cancel after pick (alternate path from Pick 5) | 5 / 5 | 0 | 0 | 0 | 5 | Return Staging → Storage; release reservation |
| Short pick 3 from a fresh 5-unit reserve | 2 / 2 | 3 | 2 | 3 | 0 | Reservation PartiallyPicked |
| Record shortfall 2 | 2 / 2 | 3 | 0 | 3 | 2 | Release unpicked remainder; backorder/discrepancy for 2 |

At every step, `unpicked_reserved ≤ eligible_on_hand`, `available ≥ 0`, and no physical unit is represented in both unpicked-reserved and picked-reserved. The cancel and short-pick rows are alternate branches, not sequential steps after dispatch.


**Transfers**

- **INV-030 (P1A)** Same-warehouse location move immediate. Inter-warehouse: Draft, Approved, In Transit, Received (partial allowed), Closed, Cancelled. Inter-branch also BRN-020–023.
- **INV-031 (P1A)** In-transit visible; not available in either warehouse.
- **INV-032 (P1A)** Ship vs receive difference → approved adjustment.

**Adjustments**

- **INV-040 (P1A)** Reason code, comment, lines. Statuses: Draft, Pending Approval, Approved, Posted, Rejected.
- **INV-041 (P1A)** Above qty/value threshold → approval; creator cannot approve.
- **INV-042 (P1A)** Posting creates matching movements; never drives available below zero.

**Counts (P2)**

- **INV-050 (P2)** Full/cycle counts; sheets; optional blind count.
- **INV-051 (P2)** Variance review → adjustment.
- **INV-052 (P2)** Variance history.

**Replenishment**

- **INV-060 (P1A)** Reorder point per product and optionally warehouse; available + incoming below → `StockBelowReorderPoint`.
- **INV-061 (P1A)** Alert once per breach until recovered.
- **INV-063 (P1A)** Inventory shall maintain an idempotent incoming projection of open PO quantities and expected dates per product and warehouse.

**Design note (INV-063/064/065):** Approval opens expected quantity; cancellation, revision, and an approved partial-close event adjust the open remainder. Receipt decrements it synchronously only if ADR-020 is approved. Incoming is a planning signal, never a stock bucket or part of Available.
- **INV-064 (P1A)** On `PurchaseOrderClosed`, Inventory removes only the remaining open-PO quantity from IncomingProjection; received quantities remain represented by stock receipts. The handler is idempotent by event id and PO revision.
- **INV-065 (P1A)** Goods receipt posting shall reduce the matching incoming quantity in the same transaction as the receipt movement and PO received counter. **NEEDS OWNER APPROVAL (OD-07); see proposed ADR-020.**
- **INV-062 (P2)** Auto draft requisition.

**Lots (P2)**

- **INV-070 (P2)** Lot/expiry on receipt when enabled.
- **INV-071 (P2)** FEFO; expired not reservable; near-expiry alerts.

**Valuation / WAC**

- **INV-080 (P1A)** Weighted-average cost; every issue records cost used.
- **INV-081 (P1A)** Stock value by warehouse, category, date.
- **INV-082 (P3)** FIFO.
- **WAC-01 (P1A)** **Costing scope:** **product within warehouse** (WAC bucket = ProductId + WarehouseId). Company-level WAC rejected for multi-warehouse ops (too coarse); lot-level WAC deferred with lots (P2). Cross-warehouse transfers do **not** blend WAC across warehouses; destination receipt uses transfer cost policy in WAC-06.
- **WAC-02 (P1A)** **Opening balances:** OpeningBalance movements set initial qty and unit cost; multiple openings allowed only while reconciling import (dry-run); opening never drives negative WAC denominator (qty=0 + cost>0 handled per WAC-08).
- **WAC-03 (P1A)** **Receipts:**  
  `new_avg = (qty_on_hand_before × avg_cost_before + receipt_qty × receipt_unit_cost) / (qty_on_hand_before + receipt_qty)`  
  using **pre-receipt** on-hand at that warehouse (eligible+ineligible physical on-hand — **all physical locations in that warehouse**, not only Storage). Receipt into Quarantine still updates WAC (it is on-hand).
- **WAC-04 (P1A)** **Issues** (sale dispatch, supplier return ship, adjustment out): cost per unit = current WAC; issue does not re-average unless qty goes to 0 (then avg cleared or held per WAC-08).
- **WAC-05 (P1A)** **Transfers:**  
  - Same warehouse location move: cost unchanged.  
  - Inter-warehouse: **TransferOut** at source WAC; **TransferIn** at source cost (transfer cost = source unit cost). Destination WAC blends via WAC-03 formula with `receipt_unit_cost = source WAC`. Loss/gain qty difference on receive = adjustment with cost impact documented in ADR.
- **WAC-06 (P1A)** Supplier returns shall issue stock at current WAC; supplier-credit variance shall not silently change WAC in P1.
- **WAC-07 (P1A)** Stock-movement reversals shall preserve original movement cost and maintain non-negative WAC with reconciled stock value. **NEEDS OWNER APPROVAL (OD-03)** for late-reversal eligibility.

**Design note (proposed only):** ADR-018 recommends rejecting a receipt reversal after later cost-affecting movements in the same bucket and using a compensating adjustment at current WAC. This is not accepted until approved.
  - Average cost must never become negative.
  - StockValue must reconcile with the accepted costing method within documented rounding tolerance (WAC-09/10).
  - Tests must cover opening with zero, transfer, eligible reversal, rejected late reversal, partial issues, reversal to zero, decimal quantities, and concurrent cost updates (D-14).
- **WAC-08 (P1A)** **Zero-stock:** when on-hand hits 0, WAC stored as 0 (or null → treated 0); next receipt establishes new WAC from receipt cost alone. **No negative WAC.**
- **WAC-09 (P1A)** WAC calculations shall retain higher-precision decimal intermediates and store the running average at scale 8.
- **WAC-10 (P1A)** **Rounding:** money display uses FIN-CALC rules; WAC internal scale per WAC-09; residual dust on final issue when qty→0 assigned to that issue (document in ADR).
- **WAC-11 (P1A)** The Inventory ADR shall define WAC cost-pool membership, transfer treatment, reversal eligibility, and concurrency.

**Availability & returns location**

- **INV-090 (P1A)** P1 shall provide a Returns location but shall not enable the customer-return workflow.
- **INV-091 (P1A)** Inventory shall reject Returns-location inbound movements without an authorized source document and reason.
- **INV-100 (P1A)** Availability query with all buckets + incoming date.

**Rules:** negative stock forbidden; base unit only; only Inventory posts movements; reservation ≤ available at commit.
