<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §4.1–4.2 · nothing changed · navigation: docs/prd/INDEX.md -->

## 4. Architecture requirements

One deployable modular monolith. Schemas aid clarity; **schemas are not security isolation** (BRN always applies).

### 4.1 Module map and dependency kinds

Dependencies are classified explicitly:

| Kind | Meaning |
| --- | --- |
| **C — Compile-time** | Module implementation references another module's **public contract assemblies** (command/query interfaces). Never references another module's tables, domain types, or internal types. **Contract assemblies are leaf assemblies: they contain no outward references to any module implementation or to other modules' contract assemblies.** |
| **R — Runtime command/query** | In-process call to a public contract implementation (still no table access). An R edge implies the caller's implementation references the callee's leaf contract assembly (counted as C/R). |
| **E — Event subscription** | Async consumer of another module's **published events** via outbox. **Event subscriptions are not compile-time dependencies.** Consumers deserialize **versioned serialized event payloads** (stable integration/event schema, OUT-05) into **consumer-owned DTOs**. Consumers **must never** reference publisher domain assemblies or publisher event-contract assemblies directly. |

**Intended topological order of the C/R graph** (dependencies before dependents):

`Platform Core → Master Data → Inventory / Integrations → Procurement → Inbound → Finance → Sales → Shipping → Fulfillment`

Fulfillment depends on Shipping for post-commit shipment creation; therefore Shipping precedes Fulfillment in the C/R topological order.

Reporting is **E-only** (no C/R edges) and **never owns operational writes** (MOD-6). AI (P2/P3) may depend on Reporting and Integrations.

| # | Module | Owns | C/R depends on | E subscribes to (publishers) |
| --- | --- | --- | --- | --- |
| 1 | Platform Core | Users, roles, permissions, tokens, audit, notifications, jobs, idempotency, sequences, approvals | — | StockBelowReorderPoint (reorder notification trigger), RequisitionApproved (optional outbox notify — §4.4), **InvoiceIssued / PaymentReceived / PaymentFailed (notification raise — §4.4)** |
| 2 | Master Data | **Company (singleton)**, branches, employees, products, UoM, suppliers, customers, warehouses, locations, tax, terms, reasons | Platform Core | — |
| 3 | Inventory | StockBalance, StockMovement, reservations, transfers, adjustments, WAC, opening imports, **incoming projection (INV-063)** | Master Data | PurchaseOrderApproved, PurchaseOrderCancelled, PurchaseOrderRevised; `PurchaseOrderClosed` (proposed, ADR-017 — INV-064) |
| 4 | Procurement | Requisitions, RFQs, quotes, awards, POs, revisions | Master Data; Inventory (read incoming/expected contracts) | — |
| 5 | Inbound | Expected deliveries, registrations, GRs, inspections, discrepancies, supplier returns | Master Data; **Inventory R** (post receipt/putaway/return commands); **Procurement R** (update PO received qty) | PurchaseOrderApproved, **PurchaseOrderCancelled** (INB-004), **PurchaseOrderRevised** (INB-005) — ADR-015; `PurchaseOrderClosed` (proposed, INB-006 — ADR-017) |
| 6 | Sales | SOs, pricing inputs, backorders, credit-check orchestration | Master Data; **Inventory R** (reserve/release/availability); **Finance contracts R** (`ICreditExposureQuery` + `IReserveCredit`/`IReleaseCredit` only) | StockReserved, ReservationFailed, InvoiceIssued, PaymentReceived, PaymentFailed (Sales local exposure projection refresh — MOD-9; never Finance's authoritative record), **PickCompleted** (status notify), **DeliveryConfirmed** (SHP-031 delivered status) |
| 7 | Fulfillment | Fulfillment orders, picks, packages, dispatch | Master Data; **Sales R** (line status); **Inventory R** (pick move, issue, release); **Shipping R** (idempotent post-commit shipment creation) | SalesOrderConfirmed, SalesOrderCancelled |
| 8 | Shipping | Carriers, shipments, tracking, delivery confirmation | Master Data; Integrations (optional adapters) | — (shipment creation is post-commit R from Fulfillment) |
| 9 | Finance | AP/AR invoices, payments, allocations, credit/debit notes, match results, balances, **credit exposure** | Master Data; **Procurement/Inbound contracts R** (ids/snapshots for match — via contracts, not tables). **No C/R dependency on Fulfillment or Sales** — dispatch data arrives only via `ShipmentDispatched` event payload persisted on the InvoiceGeneration row; sales-side data only via events | SalesOrderCancelled (reconciliation), SalesOrderConfirmed (reconciliation — authoritative open-order exposure changes only via `IReserveCredit`/`IReleaseCredit`), GoodsReceived, InspectionCompleted, SupplierReturnPosted, **ShipmentDispatched** (invoice generation), Payment events |
| 10 | Reporting | Read models, dashboards | **— (E-only; no C/R edges)** | Most business events |
| 11 | Integrations | Outbox, inbox, adapters, logs | Platform Core | — |
| 12 | AI (P2/P3) | Forecasts, assistant, anomalies | Reporting, Integrations | — |

**Acyclic rule (MOD-5):** the **module C/R graph** and the **contract-assembly graph** both have no cycles. Architecture tests (NFR-040) explicitly test both graphs — **not only raw DLL compilability**. **Sales → Finance** exists only through Finance's public credit contract assemblies. **Finance → Sales** exists only as event subscription (E), never as a compile-time reference. **Finance → Fulfillment does not exist**: Finance persists the dispatch snapshot from the `ShipmentDispatched` payload onto its InvoiceGeneration record and must not call Fulfillment contracts merely to obtain dispatch data.

### 4.2 Architecture rules

- **MOD-1 (P1A)** Each module shall own its domain tables and shall not directly read or write another module’s domain tables.
- **MOD-1a (P1A)** Infrastructure-interface usage is the sole permitted cross-module table write outside a module’s own domain tables; architecture tests enforce that no module references infrastructure tables except via the provided interfaces.

**Design note (MOD-1/MOD-1a):** Shared tables are `OutboxMessage`, `InboxMessage`, `AuditLog`, `IdempotencyKey`, `DocumentSequence`, `Notification`, and `JobRun`. Writes go only through Platform Core or Integrations infrastructure interfaces inside the transaction.
- **MOD-2 (P1A)** Cross-module reads = public query contracts; writes = public commands or published events.
- **MOD-3 (P1A)** Required events written to **outbox in the same DB transaction** as the state change; at-least-once; idempotent consumers.
- **MOD-4 (P1A)** Only Inventory posts stock movements; only Finance posts amounts owed/paid and **owns credit exposure**.
- **MOD-5 (P1A)** Module and contract-assembly dependency graphs shall be acyclic; reverse dependencies shall use public contracts or events, never table access.
- **MOD-6 (P1A)** Reporting builds read models from events; not a second system of record for operational writes; Reporting owns **no operational write path**.
- **MOD-7 (P1A)** Modules testable with doubles for dependencies.
- **MOD-8 (P1A)** No default Microservices, Redis, RabbitMQ, MediatR, CQRS frameworks, or generic workflow engine; additions need ADR (§3.1).
- **MOD-9 (P1A)** **Sales must not access Finance tables.** Credit check uses Finance’s public contracts. Sales maintains at most a **local cache/projection** of exposure for UI hints, refreshed by Finance events; **authoritative exposure is Finance’s**.
- **MOD-10 (P1A)** Three dependency kinds documented per module (table §4.1); new edges require ADR + architecture test update covering **both** the module graph and the contract graph.
