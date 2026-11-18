<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.5 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.5 Inbound Logistics (INB)

- **INB-001 (P1A)** **Approved** PO creates expected delivery (date, warehouse, lines) — created on PO **Approved**, not only when Sent (consistent with TD-9).
- **INB-004 (P1A)** On `PurchaseOrderCancelled`: cancel the open expected delivery (ADR-015). This event applies only when nothing has been received.
- **INB-005 (P1A)** On `PurchaseOrderRevised`: update open expected-delivery lines/dates and remaining expected quantities (ADR-015). The revision must not reduce ordered quantity below quantity already received.
- **INB-006 (P1A)** On `PurchaseOrderClosed` after partial receipt, close the remaining expected-delivery quantity and retain the received history; the event consumer is idempotent. Mechanism is proposed in ADR-017 and requires owner approval.
- **INB-002 (P1A)** Past-expected deliveries flagged; buyer notified.
- **INB-003 (P2)** ASN.
- **INB-010 (P1A)** Register delivery: supplier, delivery note, carrier/vehicle, timestamp, related POs.
- **INB-011 (P1A)** No matching PO → Unmatched; cannot receive into stock until matched.
- **INB-020 (P1A)** GR per PO line: received qty, unit, lot/expiry/serial if enabled, condition.
- **INB-021 (P1A)** Statuses: Draft, Received, Under Inspection, Completed, Cancelled.
- **INB-022 (P1A)** Partial receipts across multiple events until PO line complete.
- **INB-023 (P1A)** Over-receive beyond ordered+tolerance blocked without override+reason.
- **INB-024 (P1A)** **Receipt destinations:** RequiresInspection=true → **Quarantine**; false → **Receiving** then **mandatory putaway (P1)** to Storage → available without inspection document.
- **INB-025 (P1A)** GR commit: Inventory Receipt movement + StockBalance + incoming-projection decrement + PO received counters in **one TXN** (§4.5.4); publishes GoodsReceived (outbox) after commit for Reporting/Finance context only.
- **INB-026 (P1A)** Completed GR immutable; fix via reversal, supplier return, or adjustment.
- **INB-030 (P1A)** Inspection required when product flag set (supplier-specific rules P2).
- **INB-031 (P2)** Inspection plans/checklists/sampling.

**Partial inspection & sessions**

- **INB-032 (P1A)** Inspection may be **partial** and split across **multiple sessions** against the same goods receipt / received line. Per inspection line record: session id, inspector, timestamps.
- **INB-035 (P1A)** Quantity invariants per received line (and per lot if enabled):  
  `ReceivedQty` = qty posted on GR  
  `InspectedQty` = Σ accepted + Σ rejected across **all sessions**  
  `AcceptedQty` = Σ accepted  
  `RejectedQty` = Σ rejected  
  `PendingQty` = `ReceivedQty − InspectedQty`  
  Invariants:  
  - `InspectedQty = AcceptedQty + RejectedQty`  
  - `0 ≤ InspectedQty ≤ ReceivedQty`  
  - `PendingQty ≥ 0`  
  - After full inspection: `PendingQty = 0` and `InspectedQty = ReceivedQty`.
- **INB-036 (P1A)** An inspection session shall post no more than PendingQty and shall reject quantities already inspected.
- **INB-033 (P1A)** **Accepted** qty putaway to Storage → available. **Rejected** qty **stays in Quarantine** until **documented disposition**.
- **INB-037 (P1A)** **Rejection ≠ supplier return.** Rejection only moves qty to rejected bucket + quarantine hold. Supplier return is a **separate document** (INB-040) that may reference rejected qty. Other dispositions: scrap (adjustment), rework/hold, return-to-supplier — each needs reason code.
- **INB-034 (P1A)** **Every completed inspection session publishes `InspectionCompleted`** (outbox) with an explicit payload so partial sessions cannot be mistaken for final completion. Payload structure (per-line, so quantities are unambiguously tied to individual Goods Receipt lines):
  - **Session-level fields:** `InspectionSessionId`, **`IsFinal`**, `GoodsReceiptStatusAfter`, `CorrelationId`, `CausationId`, `EventVersion`.
  - **`Lines[]` — one entry per Goods Receipt line touched by the session, each containing:** `InspectionSessionId`, `GoodsReceiptId`, `GoodsReceiptLineId`, `ProductId`, `ReceivedQty`, `InspectedQtyAfter`, `AcceptedQtyDelta`, `RejectedQtyDelta`, `PendingQtyAfter`.
  - **Rules:**
    - `IsFinal = false` for a partial session; `IsFinal = true` **only** when `PendingQtyAfter = 0` **for all** inspection-required lines in `Lines[]`.
    - **Consumers must not treat a partial session as final inspection completion.** Reporting and Finance consumers must branch explicitly on `IsFinal` and/or per-line `PendingQtyAfter`.
    - Final session marks the inspection line **Completed**; partial session leaves it **PartiallyInspected** (INB-038 for GR status).
    - No separate `InspectionSessionCompleted` event is added (avoid redundant events); one event, explicit finality flag.
- **INB-038 (P1A)** Goods receipt status: may be `Under Inspection` while PendingQty > 0 for inspection-required lines; Completed when all inspection-required lines PendingQty=0 **or** no inspection required and putaway done. `GoodsReceiptStatusAfter` is carried on the event payload.
- **INB-039 (P1A)** Supplier return still P1 for rejected/shortage surplus paths; must not auto-create from inspection (INB-037).
- **INB-040 (P1A)** Supplier return: lines, reason, qty. Statuses: Draft, Approved, Shipped, Confirmed by Supplier, Closed, Cancelled.
- **INB-041 (P1A)** Shipping return posts SupplierReturn movement from Quarantine (or agreed location).
- **INB-042 (P1A)** Notifies Finance to expect credit/debit note.
- **INB-043 (P1A)** Rejection rates feed supplier reporting.
- **INB-050 (P1A)** Discrepancies: shortage, excess, wrong item, damage, missing docs with type/qty/evidence; buyer notified.
- **INB-051 (P1A)** Closing PO with shortage: reason + audit.
- **INB-060 (P1A)** **Putaway:** after acceptance or no-inspection receipt, move to Storage; simple location suggestion (capacity optimization P2).

**Business rules (P1A):** stock under inspection never available; accepted+rejected=inspected per session; completed records immutable; corrections via new documents.
