<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.4 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.4 Procurement (PRC)

- **PRC-001 (P1A)** Requisition manual or from reorder alert; lines: product, qty, unit, needed-by, destination warehouse/branch, justification.
- **PRC-002 (P1A)** Statuses: Draft, Submitted, Approved, Rejected, Returned for Change, Converted, Cancelled.
- **PRC-003 (P1A)** Approval via PLT-030.
- **PRC-004 (P1A)** Approved requisition → RFQ or direct PO per rules.
- **PRC-005 (P2)** Consolidate requisitions.
- **PRC-010 (P2)** Supplier agreements (Q-4). **PRC-011 (P2)** Agreement statuses/expiry. **PRC-012 (P2)** PO price from agreement.
- **PRC-020 (P1A)** RFQ from approved req or direct; one+ suppliers; deadline. **Simplified manual RFQ.**
- **PRC-021 (P1A)** RFQ statuses: Draft, Sent, Receiving Quotes, Closed, Cancelled; send recorded.
- **PRC-022 (P1A)** Buyer enters quotes (price, qty, lead time, validity, terms). Quote statuses: Received, Accepted, Rejected, Expired. No supplier portal.
- **PRC-023 (P1A)** Skip RFQ below amount / when rules allow direct PO.
- **PRC-030 (P1A)** Side-by-side comparison: price, lead time, terms.
- **PRC-031 (P2)** Weighted scoring.
- **PRC-032 (P1A)** Award per line or whole; non-best needs justification.
- **PRC-033 (P1A)** Multi-supplier award → one PO per supplier.
- **PRC-034 (P3)** AI award recommendation.
- **PRC-050 (P1A)** PO from awarded quote, approved req, or direct (permission). Lines: product, qty, unit, price, tax, expected date, destination warehouse/branch; totals base currency.
- **PRC-051 (P1A)** Statuses: Draft, Pending Approval, Approved, Sent, Partially Received, Received, Closed, Cancelled, Rejected.
- **PRC-052 (P1A)** Sending records time; email optional (Q-5/TD-2).
- **PRC-053 (P1A)** Change after send → numbered revision; value increase over threshold re-approves.
- **PRC-054 (P1A)** Cancel only if nothing has been received; after a partial receipt, close the unreceived remainder with a reason. Publishing `PurchaseOrderClosed` for this partial-close path is **NEEDS OWNER APPROVAL** (ADR-017/OD-02).
- **PRC-055 (P1A)** Each PO line shall track ordered, received, rejected, and invoiced quantities; Finance updates invoiced quantity through Procurement R.
- **PRC-056 (P1A)** Over-receipt tolerance percent (default 0%); exceeding tolerance blocked unless authorized override with reason.
- **PRC-057 (P1A)** Expected date history; late flag.
- **PRC-058 (P1A)** *(clarification)* PRC-055 and PRC-056 are **separate**: 055 = tracked quantities; 056 = tolerance policy.
- **PRC-060 (P1A)** Promised vs actual dates, qty, quoted vs invoiced price for reporting.
- **PRC business rules (P1A):** Active supplier only after approval; no self-approval; Closed/Cancelled accept no receipts; approved docs not edited in place.
