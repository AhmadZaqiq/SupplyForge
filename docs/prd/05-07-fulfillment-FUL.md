<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.7 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.7 Order Fulfillment (FUL)

- **FUL-001 (P1A)** Fulfillment order from reserved lines of confirmed SO, one per warehouse.
- **FUL-002 (P1A)** Statuses: Pending, Released, Picking, Picked, Packing, Packed, Ready for Dispatch, Dispatched, Cancelled.
- **FUL-003 (P1A)** Priority/due from requested delivery; late flag.
- **FUL-004 (P2)** Hold with reason.
- **FUL-010 (P1A)** Pick list: product, qty, location, lot if any, sorted by path. **Picking is workflow state**; pick confirmation moves units Storage → Staging and transfers the quantity from unpicked-reserved to picked-reserved in the same TXN.
- **FUL-011 (P1A)** Assign or claim pick lists (branch warehouse scope).
- **FUL-012 (P1A)** Confirm pick by scan or permitted manual entry; wrong product/location rejected.
- **FUL-013 (P1A)** Short pick: shortfall+reason; release remaining reservation; discrepancy; Sales notified; backorder.
- **FUL-014 (P1A)** Picked goods in **Staging** (not eligible for new reservations); the reservation tracks them as picked-reserved, outside the unpicked-reserved quantity used in the availability formula.
- **FUL-015 (P3)** Wave/batch picking.
- **FUL-020 (P1A)** Packing verifies against order line by line.
- **FUL-021 (P1A)** Package id, weight, dimensions, contents; label print P2.
- **FUL-022 (P1A)** Packing slip per shipment/dispatch (HTML/print OK; PDF P2).
- **FUL-030 (P1A)** Dispatch: time, person, carrier (manual OK).
- **FUL-031 (P1A)** Dispatch posts **Issue** from Staging; consumes reservations; **on-hand leaves at dispatch, not at pick (D-4)**.
- **FUL-032 (P1A)** Dispatch shall commit stock before the separate idempotent Shipping follow-up transaction.

**Design note (FUL-032/ADR-003):** TXN 1 commits dispatch, stock Issue movements, balances, reservation consumption, audit, and `ShipmentDispatched` outbox. TXN 2 creates the shipment via Shipping R after TXN 1 commits. A retry repeats TXN 2 by DispatchId; it never rolls back stock.
- **FUL-033 (P1A)** Partial dispatch allowed; remainder of SO stays open.
- **FUL-034 (P1A)** Dispatch note produced (HTML/print; PDF P2).
- **FUL-035 (P1A)** Dispatch record carries `DispatchStatus` only; does **not** embed invoice financial status (§5.10).
- **FUL-040 (P1A)** Cancel after pick → return to Storage via movement.
- **FUL-041 (P2)** Workload view per warehouse/operator.
- **FUL business rules (P1A):** only reserved (or short-pick path) picked; operators limited to assigned warehouses; dispatched shipment not undone (returns P2).
