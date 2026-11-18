<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.8 · nothing changed · navigation: docs/prd/INDEX.md -->

### 5.8 Shipping (SHP)

- **SHP-001 (P1A)** Carrier: name, code, service levels, contact, tracking URL pattern, active, integration type automated|manual. Credentials encrypted when present.
- **SHP-002 (P2)** Manual rate tables. **SHP-003 (P2)** Default carrier per warehouse/customer.
- **SHP-010 (P1A)** Shipping shall create one shipment per DispatchId in a post-commit TXN; multiple shipments per Sales Order are allowed.

**Design note (SHP-010/ADR-003):** `Shipment.DispatchId` is unique; duplicate calls return the existing shipment, conflicting payloads are rejected, and retries repeat the same idempotent command.
- **SHP-011 (P1A)** Manual carrier: tracking number by hand. Automated adapter P2.
- **SHP-012 (P2)** Rate quotes.
- **SHP-013 (P1A)** Estimated/actual shipping cost recorded; bill customer or expense flag (not full GL).
- **SHP-014 (P1A)** Statuses: Created, Label Ready, Picked Up, In Transit, Out for Delivery, Delivered, Delivery Failed, Returned, Exception, Cancelled (some manual in P1).
- **SHP-020 (P1A)** Tracking events: time, location, carrier code, mapped status → timeline.
- **SHP-021 (P1A)** Updates: webhook (P2), polling (P2), or **manual (P1)**; duplicates/out-of-order idempotent.
- **SHP-022 (P1A)** Carrier-specific → common status mapping table.
- **SHP-023 (P1A)** Exception when past expected date or no update beyond window; notify.
- **SHP-024 (P2)** Customer milestone emails.
- **SHP-030 (P1A)** Delivery confirmation: carrier event (P2) or manual with proof (name, time, reference minimum P1).
- **SHP-031 (P1A)** DeliveryConfirmed → SO Delivered when all shipments delivered.
- **SHP-032 (P1A)** Failed delivery: reason + retry date or return.
- **SHP-033 (P2)** Returned shipment → return inspection path.
- **SHP-034 (P3)** Carrier claims.
- **SHP business rules (P1A):** no Delivered without evidence; no cancel after pickup; shipping cost does not change stock value.
- Shipment created on dispatch after stock commit (TD-10; retry if follow-up fails).
