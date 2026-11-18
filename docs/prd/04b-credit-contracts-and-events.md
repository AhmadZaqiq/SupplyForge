<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §4.3–4.4 · nothing changed · navigation: docs/prd/INDEX.md -->

### 4.3 Credit-exposure and credit-reservation contracts

- **MOD-11 (P1A)** Finance publishes contract `ICreditExposureQuery` (**query — UI hints / non-authoritative**):
  - Input: customer id, optional as-of timestamp.
  - Output: `OutstandingInvoices`, `OverdueAmount`, `OpenOrderExposure`, `CreditLimit`, `RemainingCredit`, `IsBlocked`, `AsOf`, `Version`.
- **MOD-12 (P1A)** Finance publishes atomic reservation contract **`IReserveCredit` / `IReleaseCredit`** (or equivalent single atomic credit-reservation contract):
  - Input includes: customer id, source sales-order id, requested exposure amount, correlation id, idempotency key, and expected/version information where appropriate. An approved override reference is not yet part of the accepted contract; see OD-05 / proposed ADR-019.
  - Finance performs an **atomic check-and-set** against the authoritative customer credit exposure record (single SQL transaction / guarded update).
  - Output: reserved/released amount, new exposure version, or a typed rejection (over-limit, overdue, blocked).
- **MOD-13 (P1A)** Sales shall call `IReserveCredit` during SO confirmation and shall fail closed unless the atomic reservation succeeds.
- **MOD-14 (P1A)** Cancellation or reduction of a confirmed SO **releases** the corresponding credit reservation atomically via `IReleaseCredit`; retries of confirm/release are **idempotent** (idempotency key + expected version).
- **MOD-15 (P1A)** Finance shall maintain authoritative credit exposure through atomic reserve/release commands and invoice/payment updates.

**Design note (MOD-15/17/18):** Open-order exposure changes only through `IReserveCredit`/`IReleaseCredit`. Invoice issuance releases the proportional reservation for invoiced quantity in the same transaction; any remainder is released on SO close/cancel. Finance consumes SalesOrderConfirmed/Cancelled events for reconciliation only. `InvoiceIssued`/`PaymentReceived` update invoice and overdue components; consumers are idempotent.
- **MOD-17 (P1A)** Invoice issuance shall release the proportional open-order exposure for invoiced quantity in the same Finance transaction.
- **MOD-18 (P1A)** Finance shall consume SalesOrderConfirmed/Cancelled events idempotently for reconciliation only and shall not double-count atomic reserve/release changes.

- **MOD-16 (P1A)** Contract versioned; additive changes only within a minor; breaking change = new contract interface name/version .

### 4.4 Event catalog

| Event | Publisher | Delivery | Consumers (E unless noted) |
| --- | --- | --- | --- |
| RequisitionApproved | Procurement | Sync UI + optional outbox notify | UI only / notifications |
| PurchaseOrderApproved | Procurement | **Outbox** | Inbound (create expected delivery), **Inventory (open incoming signal — INV-063)**, Reporting |
| PurchaseOrderCancelled | Procurement | **Outbox** | Inbound (**cancel open expected delivery** — INB-004; precondition: nothing received per PRC-054), **Inventory (clear incoming signal)**, Reporting |
| PurchaseOrderRevised | Procurement | **Outbox** | Inbound (update expected delivery — INB-005), Inventory (adjust incoming signal — INV-063), Reporting |
| `PurchaseOrderClosed` (proposed; **NEEDS OWNER APPROVAL**) | Procurement | **Outbox if approved** | Inbound (close remaining expected delivery — INB-006), Inventory (clear remaining incoming — INV-064), Reporting |
| GoodsReceived | Inbound | **Outbox after GR commit succeeds** | **Reporting**; **Finance** (AP/matching context only, if required). **Not** Procurement (its received counters are updated synchronously in the GR transaction via R — §4.5.4); **not** Inventory for the primary receipt movement (Inventory already posted it synchronously via R) |
| InspectionCompleted | Inbound | **Outbox** after each session commit | Reporting; Finance (must branch on `IsFinal`/`PendingQtyAfter` — INB-034); putaway already via **R** in inspection commit |
| SupplierReturnPosted | Inbound | **Outbox** | Finance (expect credit note), Reporting |
| SalesOrderConfirmed | Sales | **Outbox** | Inventory reserve is **R sync** in confirm path; outbox → Finance (**reconciliation of authoritative credit exposure only** — open-order exposure changes via `IReserveCredit`), Fulfillment create, Reporting |
| StockReserved / ReservationFailed | Inventory | **Outbox** (after reserve TXN) | Sales line update (**sync R result in the confirm path is authoritative for the request**; Sales also consumes the event idempotently for projection/audit refresh — §4.1); Reporting |
| SalesOrderCancelled | Sales | **Outbox** | Inventory release **R sync**; Fulfillment cancel E; Finance **reconciliation** E (release of credit is sync via `IReleaseCredit`) |
| PickCompleted | Fulfillment | **Outbox** | Sales notify; Reporting |
| ShipmentDispatched | Fulfillment | **Outbox** after dispatch TXN | Finance invoice generation (E, async); Reporting. Shipping creation is the separate post-commit R follow-up in §4.5.4, not an E subscription. |
| DeliveryConfirmed | Shipping | **Outbox** | Sales delivered status E |
| StockBelowReorderPoint | Inventory | **Outbox** | Notifications; draft req P2 |
| InvoiceIssued / PaymentReceived / PaymentFailed | Finance | **Outbox** | Sales exposure E (local projection); notifications; Reporting |

---
