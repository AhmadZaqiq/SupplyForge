<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.6 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.6 Sales (SAL)

- **SAL-001 (P1A)** Customer context incl. credit fields **from `ICreditExposureQuery`** (MOD-11), not Finance tables.
- **SAL-010 (P2)** Sales quotations. **SAL-011 (P2)** Quote → SO conversion keeping prices.
- **SAL-020 (P1A)** P1 line price defaults to the product's configured default price; customer groups and price lists are P2 and are not inputs to P1 price resolution. Customer-specific price overrides are not defined for P1 and remain an Open Question.
- **SAL-021 (P2)** Price lists with validity, tiers.
- **SAL-022 (P1A)** P1 supports line-level discounts only; any non-zero line discount requires an approved Platform Core approval decision before SO confirmation. Role-specific discount ceilings and order-level discounts are not defined for P1. **NEEDS OWNER APPROVAL** — see Owner Decision OD-04.
- **SAL-023 (P1A)** Tax per line from configured rate (tax-exclusive — §6.6.2).
- **SAL-024 (P1A)** Confirmed order prices frozen.
- **SAL-030 (P1A)** SO: customer, addresses, terms, requested delivery, notes, lines (product, qty, unit, price, discount, tax), **branch** (selling branch).
- **SAL-031 (P1A)** Statuses: Draft, Confirmed, In Fulfillment, Partially Shipped, Shipped, Delivered, Closed, Cancelled.
- **SAL-032 (P1A)** SO confirmation shall require valid lines, an address, an Active customer, and a successful atomic credit reservation.
- **SAL-033 (P1A)** Availability while entering; re-checked at confirm.
- **SAL-040 (P1A)** Availability per line/warehouse: available, reserved, in-transit, incoming+date.
- **SAL-041 (P1A)** Confirm requests reservation; line → Reserved / Partially Reserved / Not Reserved.
- **SAL-042 (P1A)** Short line: wait backorder, ship partial, or cancel line.
- **SAL-043 (P2)** Auto-reserve backorders oldest-first on receipt.
- **SAL-044 (P1A)** Warehouse: customer default or best availability per company rule (configurable; default document warehouse).
- **SAL-050 (P1A)** Before picking: line changes adjust reservations.
- **SAL-051 (P1A)** After picking starts: reduce/cancel needs warehouse confirmation.
- **SAL-052 (P1A)** Cancel with reason; release reservations; cancel fulfillment; if invoiced → credit note path (FIN).
- **SAL-060 (P1A)** Credit gate via contracts MOD-11/12/13: **atomic reservation at commit**; block when over limit or overdue beyond N days (config). Query-only data is a UI hint, not the decision.
- **SAL-061 (P1A)** An authorized credit override requires an approved Platform Core decision, a reason, and an audit record; the approved decision must be validated by the Finance credit-reservation contract before reservation. Contract details are **NEEDS OWNER APPROVAL** (OD-05).
- **SAL-070 (P2)** Customer returns full process (not in P1).
- **SAL-071 (P2)** Return receive → CustomerReturn movement to Returns (guarded INV-091).
- **SAL-072 (P2)** Resolution refund/credit/replacement.
- **SAL-073 (P2)** Return rate reporting.
- **SAL-074 (P1A)** Sales **must not** read Finance tables (MOD-9). Credit UI data via query contract or Sales-owned projection from events.
- **SAL business rules (P1A):** Blocked/On Hold customer cannot confirm; FIFO by confirmation time; delivered orders immutable; Sales never posts stock directly.
