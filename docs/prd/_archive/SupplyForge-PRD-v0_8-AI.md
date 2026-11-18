# SupplyForge — Product Requirements Document

**Edition:** AI-optimised. Content identical to v0.8 except: the Appendix C acceptance table (a verbatim restatement of §5–§6 requirements) was removed, the grouped-alias appendix was compacted and renumbered as Appendix C, §13.2 numbering and a §10.2 sentence were fixed, and §0 was added. No requirement text was changed.  

**Version:** 0.8  
**Date:** 15 November 2026  
**Product:** SupplyForge  
**Audience:** Product owner, software architect, backend developers, testers, and delivery leads.

## 0. How to use this document (AI agents and reviewers)

1. **Authority.** This PRD is the requirements authority. Accepted mechanisms live in `ADR/ADR-001`…`ADR-016`. `ADR-017`…`ADR-019` are proposals, not decisions. Companion documents never override this PRD (§1.1, §1.4).
2. **Scope per task.** Work only on the slice or gate you are assigned (§11). `P1A` and `P1B` are delivery stages inside P1 (§1.3, §11.3). Do not implement `P2` or `P3` requirements, including AI (§5.12) and advanced integrations (§5.13, §7), unless the task says so.
3. **Stop and ask, do not decide.**
   - Text marked `NEEDS OWNER APPROVAL`, or a dependency on a proposed ADR: do not implement that behaviour; ask the product owner.
   - Questions marked `Open-PO` in §13.1: if a `Default` is stated there or in §3.3 (`TD-n`), implement the default; otherwise ask.
   - A requirement that relies on an ADR or Decision Register entry you were not given: ask for it instead of inventing the mechanism.
4. **Invariants always apply.** §6.1 global invariants, §6.3 negative stock, §4.2 architecture rules and module dependency kinds (§4.1), the credit-reservation contract (§4.3), idempotency, transactions and outbox (§4.5), and branch isolation with the 404/403 policy (BRN-011…014). Sales must not read Finance tables (SAL-074).
5. **Traceability.** Put the requirement ID in test names (NFR-051) and in commit or PR titles.
6. **Acceptance.** A requirement is accepted when tests named with its ID verify its statement. §10.1 lists the cross-cutting scenarios that must pass.
7. **Reference forms.** `D-n` → §12, `Q-n` → §13.1, `A-n` → §3.2, `TD-n` → §3.3, PA/PC items → `docs/Decision Register.md`. `OD-n` marks a decision awaiting owner approval (e.g. OD-03, OD-05): treat it as rule 3 above.

**Suggested reading order for a slice:** §1.3 → §2.5 → §3 → §4 → §6 → the §5 sections for that slice → §11 → §12–§13.

**Document map:** 1. Introduction · 2. Product overview · 3. Constraints, assumptions, and temporary defaults · 4. Architecture requirements · 5. Functional requirements · 6. Cross-cutting requirements · 7. External integrations · 8. Non-functional requirements · 9. Data requirements · 10. Testing and acceptance · 11. Delivery plan · 12. Decision and ADR index · 13. Open questions · Appendix A. Glossary · Appendix B. Conceptual diagram alignment · Appendix C. Grouped requirement-ID aliases

---

## 1. Introduction

### 1.1 Purpose

This specification defines the product outcomes, functional requirements, constraints, quality attributes, data rules, acceptance criteria, delivery gates, and unresolved decisions for SupplyForge. It is the requirements authority for implementation. Architecture Decision Records (ADRs) are authoritative for accepted mechanisms; companion documents are navigation aids and must not override this specification.

### 1.2 Scope of this document

This is a documentation-only baseline. It does not claim production certification, legal compliance, or completed implementation. Every requirement retains its identifier; newly introduced requirements use the next available identifier in their prefix.

### 1.3 Requirement conventions

- **ID** = prefix + number (example `INV-014`). Prefixes: `BRN`, `PLT`, `MD`, `INV`, `PRC`, `INB`, `SAL`, `FUL`, `SHP`, `FIN`, `RPT`, `AI`, `INT`, `NFR`, `MOD`, `DM`, `TXN`, `IDEM`, `OUT`, `SEC`, `WAC`, `TD` (temporary defaults).
- **Priority**: **P1A / P1B** = delivery stage inside P1 (see §11); **P2** = after P1 stable; **P3** = future / non-goal for first release. Priority tags **P1** mean “somewhere in P1A or P1B” and are mapped in §11. **Every requirement is tagged.**
- `D-n` → §12 · `Q-n` → §13.1 · `A-n` → §3.2 · `TD-n` → §3.3 · `ADR-nnn` → `ADR/` folder · PA/PC items → `docs/Decision Register.md`.

---

### 1.4 Authority and references

- Requirements and priorities: this PRD.
- Accepted mechanisms: `ADR/ADR-001` through `ADR-016`.
- Proposed mechanisms requiring owner approval: `ADR/ADR-017` through `ADR-019`; they are not accepted decisions.
- Conceptual diagram: `SF.png`, read-only; alignment is described in Appendix B.



## 2. Product overview

### 2.1 Problem

- Products are promised or sold that are not physically available.
- Purchasing decisions lack a trustworthy stock view.
- Short/damaged deliveries recorded as complete.
- Stock drifts from physical counts with no explainable history.
- Late orders with no visibility of why.
- Payments recorded twice or on the wrong invoice.
- No clear who/what/when audit; weak branch boundaries.

### 2.2 Vision

One platform to plan, buy, receive, store, sell, ship and get paid — every step traceable, every quantity explainable from an immutable movement history, access limited to authorized branches.

### 2.3 Goals

**Product**

- **G-1** Full goods lifecycle: supplier → purchase → receipt → stock → sale → shipment → invoice → payment → report.
- **G-2** Stock trustworthy: never negative (P1), never over-reserved, explainable from movements.
- **G-3** One company with **seeded three branches**, branch-scoped operational access, company-wide access only when explicitly authorized.
- **G-4** AI and advanced external integrations **outside** the P1 lifecycle dependency chain.
- **G-5** Optional external failures never lose committed business events.

**Engineering**

- **G-6** Significant decisions recorded as ADRs.
- **G-7** Modular monolith, clear boundaries, real SQL Server tests, CI, OpenAPI.
- **G-8** Size follows need; estimates are checks, not targets.

### 2.4 Success criteria

- **P1A** delivers an early runnable slice (foundations → buy/receive → sell/reserve → dispatch with manual money path) with explicit gates (§11).
- **P1B** completes money, inspection depth, thin reporting, hardening.
- Concurrent stock tests: never negative; same units never double-reserved.
- Branch IDOR tests fail closed on all API paths.
- Dispatch success is **distinguishable** from invoice-generation success; each dispatch yields **at most one** invoice-generation result.
- Audit captures before/after with sensitive-field redaction.
- Each P1 requirement maps to a test or documented waiver.

---

### 2.5 Scope and non-goals

#### 2.5.1 In scope

| Area | Stage | Later |
| --- | --- | --- |
| Identity, roles, branch auth, audit | P1A | 2FA P2; IdP P3 |
| Company singleton + **seed three branches**, master data | P1A | Import UX P2 |
| Inventory ledger/balances/reservations/transfers/adjustments/opening/WAC | P1A | Lots P2; counts P2; FIFO P3 |
| Procurement: req → RFQ → compare → approve → PO | P1A | Agreements P2 |
| Inbound: delivery, receive, **partial inspection**, putaway | P1A | Plans/sampling P2; ASN P2 |
| Sales: SO, pricing, availability, reservation, **atomic credit reservation via contract** | P1A | Quotes P2; price tiers P2 |
| Fulfillment + Shipping manual path + **dispatch** | P1A | Carrier adapters P2 |
| **Customer returns workflow** | **P2** (Returns **location type** exists in P1A) | — |
| Finance: supplier invoice record, AP match, payments, per-dispatch AR invoices, allocations, credit notes | **P1B** (minimal manual customer invoice optional in P1A — TD-6) | Provider P2; reminders P2 |
| Outbox, idempotency, notifications | P1A | Templates P2 |
| Thin reports/dashboard | P1B | Full suite P2 |
| AI | **No** | P2/P3 |

#### 2.5.2 Out of scope

Multi-tenant SaaS; manufacturing; full GL/statutory filing; HR/payroll; online store/POS/EDI; portals; fleet; native mobile; country e-invoicing; autonomous AI actions; generic workflow/CQRS/microservices platforms.

#### 2.5.3 Future multi-company

Must not unnecessarily block later multi-company evolution; any such work requires **ADR + migration plan**. v1 does not implement it and does **not** spray speculative tenancy columns.

---

### 2.6 Users, roles, and permissions

#### 2.6.1 Roles

| Role | Main responsibilities | Must not |
| --- | --- | --- |
| Company Executive | Cross-branch dashboards, high-threshold approvals | Change stock; edit posted docs |
| Branch Manager | Assigned-branch ops oversight and approvals | Self-approve; edit posted financials |
| Procurement Manager | Approve req/PO; company supplier master | Receive goods; self-approve |
| Procurement Officer | Req, RFQ, quotes, draft PO | Approve PO; post invoices |
| Warehouse Manager | Warehouses/locations in scope; approve transfers/adjustments | Create PO/invoices |
| Warehouse Operator | Receive, putaway, pick, pack, dispatch | Change prices; approve adjustments |
| Quality Inspector | Inspect accept/reject | Edit POs |
| Sales Representative | Customers, SOs, availability in branch | Edit stock; change suppliers |
| Accountant | Invoices, payments, matching (company or branch finance scope) | Change stock/order qty |
| Company Administrator | Users, roles, settings, branch assignment | Bypass audit; edit posted financials |
| Integration Client | Scoped API credentials | Act outside scopes |

No Platform Operator; no multi-company admin surface.

#### 2.6.2 Access principles

- **PLT-ROLE-1 (P1A)** Fine-grained permission catalogue; roles = bundles; admin customizable within catalogue.
- **PLT-ROLE-2 (P1A)** Separation of duties: requester ≠ approver; payment recorder ≠ invoice approver above threshold.
- **PLT-ROLE-3 (P1A)** Branch (and optional warehouse) scope; out-of-scope data invisible and unwritable.
- **PLT-ROLE-4 (P1A)** Company-wide permissions separate from branch operational permissions.
- **PLT-ROLE-5 (P1A)** Scope/permission changes effective within configurable delay (default 5 min) or on token refresh (ADR).
- **PLT-ROLE-6 (P2)** 2FA; IdP P3.

---

### 2.7 Company and branch model

#### 2.7.1 Model

- **Company is a singleton configuration record** (one row / one bounded context instance): legal profile, base currency, timezone, fiscal year, settings. It is **not a tenant**, not provisionable, not multi-instance in v1 (D-2, A-4).
- **Three branches are initial seed data**, not a hard-coded technical maximum (MD-002, A-2). The schema allows additional branches via configuration if the product owner later adds them; v1 seed and acceptance use exactly three.
- Warehouses belong to branches; locations belong to warehouses.
- Branch-owned documents carry `BranchId`: PO, GR, inspection, supplier return, SO, fulfillment, shipment, transfer, adjustment, counts, AP/AR invoices, payments, credit notes (as applicable).
- Company-shared master data has no `BranchId`: products, categories, UoM, suppliers, customers, tax rates, payment terms, reason codes, employees, users, **Company** singleton.

#### 2.7.2 Shared vs branch-specific

| Shared (company-wide) | Branch-specific |
| --- | --- |
| Products, categories, units, barcodes | Warehouses, locations |
| Suppliers, customers, contacts | Stock balances, movements, reservations |
| Tax rates, payment terms, reason codes | Req, RFQ (destination), PO, GR, inspection, supplier returns |
| Users, roles, permission catalogue | SO, fulfillment, shipments |
| **Company** singleton (currency, fiscal year) | Transfers, adjustments, counts |
| Document type templates | AP/AR docs & payments (see §5.9.2 for payment recording branch) |
| Default price lists | Document sequences (may be per branch — D-15) |

#### 2.7.3 Authorization scope

- **BRN-001 (P1A)** Branch-owned records readable/writable only within caller’s branch scope unless company-wide permission for that action.
- **BRN-002 (P1A)** Enforced on reads, writes, searches, list filters, reports, exports, nested IDs, background jobs.
- **BRN-003 (P1A)** Object-level: load-by-id verifies ownership before return/mutation. **ID bypass forbidden.**
- **BRN-004 (P1A)** Function-level: endpoint declares permissions; deny by default.
- **BRN-005 (P1A)** Property-level: payment instrument details and cross-branch aggregates gated.
- **BRN-006 (P1A)** Lists/reports filter by effective scope; cross-branch views need explicit permission + audit.
- **BRN-007 (P1A)** Background jobs re-apply scope for user-visible outputs; no privilege escalation from a branch user context.
- **BRN-008 (P1A)** Exports use the same filters/field limits as interactive queries.
- **BRN-009 (P1A)** Last active Company Administrator cannot be deactivated.
- **BRN-010 (P1A)** Users assigned one+ branches or company-wide scope flag.

**HTTP status policy (P1A) — consistent 404/403:**

- **BRN-011 (P1A)** If the resource **does not exist** → **404**.
- **BRN-012 (P1A)** The API shall return 404 for branch-isolated resources outside caller scope, except when diagnostic foreign-branch permission requires 403.
- **BRN-013 (P1A)** If the resource is **in scope** but the caller lacks the **function permission** for the action → **403**.
- **BRN-014 (P1A)** Authenticated but insufficient role for a **known in-scope** document action → **403**; unauthenticated → **401**.
- Rationale: branch isolation prefers non-disclosure (404); functional denial on visible resources is explicit (403). Tests cover both (§10).

#### 2.7.4 Cross-branch transfers

- **BRN-020 (P1A)** Inter-warehouse transfer (same or different branch). Different branches = inter-branch.
- **BRN-021 (P1A)** Inter-branch requires approval on both branches or company-wide approver (configurable; default both-or-company).
- **BRN-022 (P1A)** In-transit not available in either branch.
- **BRN-023 (P1A)** Destination receive + putaway per §5.5.
- **BRN-024 (P2)** Discrepancy via adjustment with dual-branch audit visibility.

#### 2.7.5 Business rules

- No create/update outside scope; scope changes do not rewrite history; branches deactivated not deleted while holding stock/open docs.

---

### 2.8 End-to-end workflow

#### 2.8.1 Core cycle

Stages marked **[P1A]** vs **[P1B]**:

1. Reorder alert **[P1A]** → 2. Requisition **[P1A]** → 3. RFQ/compare **[P1A]** → 4. Approval **[P1A]** → 5. PO sent **[P1A]** → 6. Delivery register **[P1A]** → 7. Receive ± inspection (partial OK) **[P1A]** → 8. Putaway → available **[P1A]** → 9. SO confirm + **atomic credit reservation** **[P1A]** → 10. Reserve **[P1A]** → 11. Pick/pack to staging **[P1A]** → 12. Dispatch (stock issue) **[P1A]** → 12b. **Invoice generation for dispatched qty (async, idempotent)** **[P1B]** (TD-6 if manual stub in P1A) → 13. Manual tracking/delivery confirm **[P1A]** → 14. AP/AR payments, thin reports **[P1B]**.

#### 2.8.2 Failure and edge scenarios

| Scenario | Required behavior |
| --- | --- |
| Delivery short/damaged | Post actual; shortage on PO; reject → quarantine; **reject ≠ auto supplier return** (explicit document) |
| Competing reservations | Exactly one wins; no double-reserve |
| Cancel after reservation | Release; picked → return movement |
| Short pick | Split; notify; backorder |
| Duplicate payment/provider event | Idempotent no-op |
| Supplier invoice mismatch | Block pay; override + reason |
| Credit limit | Block confirm unless override; **atomic `IReserveCredit` at commit (MOD-12/13)** |
| Optional external down | Business commit OK; queue/retry |
| Partial dispatch | Invoice only that dispatch qty; later dispatch → another invoice |
| **Dispatch OK, invoice generation fails** | Dispatch remains **Dispatched**; invoice-gen status **Failed/Retry**; recovery job; no stock rollback |
| **Invoice generation retried** | At most one invoice per dispatch id |
| **Open-order exposure converted at invoice** | Invoice TXN releases proportional `IReleaseCredit` (MOD-15); amount counted once — open-order until invoiced, outstanding after; never both; SO close releases any residual |
| Same client request twice | Idempotency key replay |
| Customer return in P1 | **Not available**; Returns location cannot receive untraceable CustomerReturn movements |
| Inspection partial | PendingQty remains in Quarantine until further session or disposition |
| Cross-branch payment | Recording branch ≠ invoice branch allowed only with policy in §5.9.2 |

---

## 3. Constraints, assumptions, and temporary defaults

### 3.1 Fixed technology baseline

- C#, **.NET 10**, ASP.NET Core Web API, **SQL Server**, **EF Core 10**, **Modular Monolith**, **Docker Compose**, **xUnit**, **real SQL Server integration tests**, **OpenAPI**, CI, **ADRs**.
- **Not by default:** Microservices, Redis, RabbitMQ, MediatR, CQRS frameworks, unnecessary abstraction layers (MOD-8).


### 3.2 Assumptions

| ID | Assumption |
| --- | --- |
| A-1 | USD scale 2 demo; tax-exclusive; discount then tax; half-away-from-zero line rounding — confirmable |
| A-2 | **Seed three branches**; schema not limited to three; v1 acceptance uses three |
| A-3 | Performance figures starting targets (workloads NFR-025), not SLAs |
| A-4 | Single legal entity; Company = singleton config; no intercompany |
| A-5 | Employee directory non-HR |
| A-6 | Company Administrator replaces Tenant Admin |
| A-7 | Picking is workflow not location type |
| A-8 | SF.png Multi-Tenancy outdated |
| A-9 | English-only P1 messages |
| A-10 | Portfolio backend, not certified ERP |

### 3.3 Temporary defaults

**Not confirmed product decisions.** Each has owner, reason, impact, deadline/stage. Promote to decision or replace when decided; do not treat as permanent business rules.

| ID | Temporary default | Owner | Reason | Impact if wrong | Decide by |
| --- | --- | --- | --- | --- | --- |
| TD-1 | Base currency USD scale 2 | Product owner | Unblocks money code | Rework scale/rounding | Before B2 (Q-1) |
| TD-2 | Email optional; manual mark-sent OK | Product owner | Avoid email dependency in A2 | Notification UX | Before B4 (Q-5) |
| TD-3 | Credit check fails closed if query unavailable (no stale cache accept) | Architect | Safety | Confirm friction | A3 design review |
| TD-4 | No WAC true-up on supplier credit variance | Architect + PO | Avoid silent cost mutation | Margin noise | Before B2 (Q-12) |
| TD-5 | WAC running average scale 8; reversal eligibility and correction path pending OD-03 | Architect + Product owner | Keeps WAC implementation blocked from silently assuming cost-layer behavior | Cost reconciliation and correction workflow | Before A1 costing code |
| TD-6 | P1A may omit auto invoice generation; manual invoice stub optional | Product owner | Early E2E without B1 | Demo gap | Start of B1 |
| TD-7 | No Adjustment→Returns path in P1 (strict INV-091) | Architect | Prevent untraceable returns | Manual edge cases | Before B3 (Q-13) |
| TD-8 | Cross-branch payment allocation requires `finance.payment.cross_branch` | PO default | Safer default | Friction for multi-scope accountants | Before B2 (Q-11) |
| TD-9 | Expected delivery created on PO **Approved** (not Sent) | Architect | Simpler inbound | Timing nuance | A2 design |
| TD-10 | Shipment row is created in a second TXN after stock commit; retry is idempotent by DispatchId (ADR-003) | Architect | Stock is not blocked by Shipping | Retry operation must be idempotent | Mechanism accepted; test before A4 |

---

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

### 4.5 Idempotency, transactions, and outbox

#### 4.5.1 Idempotency

- **IDEM-01 (P1A)** Commands creating stock movements, money documents, payments, dispatches, invoices, or posting transitions **accept an idempotency key**.
- **IDEM-02 (P1A)** Key scope: `Idempotency-Key` header/body unique per operation type + user/client + logical resource; store request hash + response reference.
- **IDEM-03 (P1A)** Same key + same payload → original result replay; no new side effects.
- **IDEM-04 (P1A)** Same key + different payload → **409/422**; do not execute.
- **IDEM-05 (P1A)** Concurrent same-key requests: exactly one execution; other gets replay — no double post.
- **IDEM-06 (P1A)** Retention default retention **72 hours**; dispatch-to-invoice derived keys are retained for **at least 7 days** (ADR-003).
- **IDEM-07 (P1B)** Dispatch → invoice: key derived from **dispatch id** (server-side) so retries cannot double-invoice (FIN-020a/028).

#### 4.5.2 Hybrid event and transaction model

**MOD-TXN-01 (P1A)** Two modes only — **no message broker, no distributed transactions**:

| Mode | When | Consistency |
| --- | --- | --- |
| **Synchronous orchestration (R)** | Operation must succeed or fail with the user-visible business result in one request: stock post, reserve, SO confirm (incl. **atomic credit reserve/release**), GR post, inspection session post, dispatch stock issue, payment record **(P1B)**, approval transitions | Single DB transaction (or tight sequential TXNs with compensating commands); immediate success/failure |
| **Asynchronous outbox (E)** | Post-commit effects: notifications, external integrations (email/carrier/payment provider), Reporting projections, **customer invoice generation from dispatch**, exposure refresh side-effects, analytics | Eventually consistent; at-least-once; idempotent consumers |

**MOD-TXN-02 (P1A)** Never call external network inside a DB transaction.

**MOD-TXN-03 (P1A)** A business operation may combine: sync domain TXN + outbox rows; async workers only after commit.

#### 4.5.3 Outbox

- **OUT-01 (P1A)** Outbox owned by **Integrations** infrastructure; written by publisher modules **in the same transaction** as domain change.
- **OUT-02 (P1A)** Delivery **at-least-once**; consumers idempotent (inbox dedupe by message/event id — `InboxMessage` shared-infrastructure table, MOD-1).
- **OUT-03 (P1A)** Retry with backoff; dead-letter after max attempts; manual retry visible.
- **OUT-04 (P1A)** Events carry: event id, type, version, occurred time, correlation id, causation id, branch id if relevant, payload.
- **OUT-05 (P1A)** Schema version field; additive tolerance; breaking → new event version + tests.
- **OUT-06 (P1A)** Correlation id: API → jobs → events → logs; causation id links to causing message.
- **OUT-07 (P1A)** In-process pub/sub after outbox delivery OK; **no RabbitMQ/broker**.

#### 4.5.4 Transaction boundaries

| Operation | Boundary | Failure behavior | Owner |
| --- | --- | --- | --- |
| **Goods Receipt post** | **One TXN:** GR document lines + Inventory Receipt movements + StockBalance + PO received counters (via Inbound→Inventory **R**, Inbound→Procurement **R**) + audit + outbox `GoodsReceived` written in the same TXN (delivered after commit) | Whole GR post rolls back; user retries with idempotency key. `GoodsReceived` is emitted **only after the GR commit succeeds** | Inbound orchestrates; Inventory/Procurement participate in-process |
| **Inventory posting** (putaway, adjustment, transfer legs) | **One TXN:** movements + balances + outbox (if event required) + audit | Rollback all; no partial balance | Inventory |
| **SO confirm (incl. atomic credit)** | **One TXN:** SO status + **`IReserveCredit` atomic check-and-set (MOD-12/13)** + reservation command **R** + audit + outbox `SalesOrderConfirmed`. Credit query alone is **not** sufficient protection; confirm commits only if reservation succeeds | Confirm fails if reserve/inventory fails or contract unavailable (fail closed, TD-3); no silent confirm; duplicate retry idempotent (no double reserve) | Sales orchestrates; Finance participates via credit contract in-process |
| **SO cancel / reduce** | **One TXN:** status change + **`IReleaseCredit` atomic release (MOD-14)** + inventory release **R** + audit + outbox `SalesOrderCancelled` | Release failure fails the cancel; no stranded credit reservation | Sales orchestrates; Finance via contract |
| **Dispatch** | **TXN 1:** dispatch doc + Issue movements + balances + reservation consume + audit + outbox `ShipmentDispatched`. **TXN 2:** after TXN 1 commits, Fulfillment calls the Shipping create contract; the command is idempotent by DispatchId and a retry job repeats TXN 2 on failure. | Stock success is authoritative; shipment/invoice are retried independently | Fulfillment (stock); Shipping (shipment row); Finance (invoice gen async) |
| **Invoice generation** | **Separate TXN(s) after dispatch commit:** claim/transition the single `InvoiceGeneration` row (**unique DispatchId**) + create invoice + lines + AR + **proportional `IReleaseCredit` of open-order exposure for the invoiced qty (MOD-15 — conversion, not double-count)** + outbox `InvoiceIssued` + status Succeeded (+ optional attempt-history row) | Dispatch **unchanged**; generation Failed + retry updates the **same** row; never a second generation row or invoice after Succeeded; release retried idempotently with the row transition | Finance |
| **Payment record (P1B)** | **One TXN:** payment record + idempotency claim + (when in the same operation) allocations + allocation caps (FIN-CALC-14/15) + unallocated-credit calculation + balance projection update + audit + outbox `PaymentReceived` (if applicable). Balance projection and exposure components may be updated in the same TXN (Finance-owned) — **no external calls** | Whole payment TXN rolls back; retry with idempotency key is a no-op replay | Finance |
| **Inspection session** | **One TXN:** session lines + inspected counters + putaway R for accepted + audit + outbox `InspectionCompleted` (with `IsFinal` payload) | Partial session all-or-nothing; no double-inspect | Inbound |

**MOD-TXN-04 (P1A)** Cross-module **R** calls inside a TXN are in-process method calls sharing the ambient transaction (same SQL Server); they are **not** distributed transactions.

**MOD-TXN-05 (P1A)** If a sync R dependency is unavailable (exception), the orchestrating TXN fails; no half-committed multi-module state.

---

## 5. Functional requirements

### 5.1 Platform Core (PLT)

**Identity**

- **PLT-010 (P1A)** Users admin-created (invite optional); email+password; salted strong KDF hashes (ADR).
- **PLT-011 (P1A)** Access + refresh tokens; refresh **rotated**; revoked on logout/password change/deactivation/reuse detection.
- **PLT-012 (P1A)** Lockout + rate limit on auth.
- **PLT-013 (P1A)** Password reset expiring single-use link; admin reset fallback (Q-5).
- **PLT-014 (P2)** 2FA. **PLT-015 (P3)** External IdP.

**Authorization**

- **PLT-020 (P1A)** Permission catalogue; role bundles; multi-role union.
- **PLT-021 (P1A)** Branch/warehouse scope (BRN).
- **PLT-022 (P1A)** Endpoint permission metadata; deny default; coverage tests.
- **PLT-023 (P1A)** Record-level rules on top of permissions.
- **PLT-024 (P1A)** Nested resources authorize via parent scope.

**Approvals**

- **PLT-030 (P1A)** Policies by document type: amount, category, branch; ordered steps.
- **PLT-031 (P1A)** Approve/reject/return + comment; recorded.
- **PLT-032 (P1A)** No self-approval; no double-step by same person.
- **PLT-033 (P2)** Delegation/escalation.
- **PLT-034 (P1A)** Engine for req, PO, adjustment, large discount, credit override, supplier payment thresholds.

**Audit**

- **PLT-040 (P1A)** Create/update/void/status/permission/sign-in with user, time, entity, **before/after values**, correlation id, source.
- **PLT-041 (P1A)** Immutable via app API; searchable (entity, user, action, date, branch).
- **PLT-042 (P2)** Export/retention/integrity; duration Q-8.

**Audit capture & redaction**

- **PLT-043 (P1A)** Before/after capture: for updates, store changed-field map (field → old, new); for creates, new values; for status changes, status old/new + reason. Full-row snapshots optional P2.
- **PLT-044 (P1A)** **Sensitive-field redaction** in audit payloads and logs: passwords, token hashes, webhook secrets, API key secrets, full payment instrument numbers (keep last4), national ids if ever stored. Redaction list configured in code + tests.
- **PLT-045 (P1A)** Audit query API respects BRN scope (company-wide permission for cross-branch audit).

**Notifications**

- **PLT-050 (P1A)** In-app notifications: approval requested, low stock, delayed shipment, payment failure, invoice-gen failure (new).
- **PLT-051 (P1A)** Email via Integrations when configured; else visible pending.
- **PLT-052 (P1A)** Async retry; failures visible.
- **PLT-053 (P2)** Custom templates.

**Reliability & common**

- **PLT-070 (P1A)** Outbox + idempotent consumers; stock/money commands accept idempotency keys (§4.5).
- **PLT-071 (P1A)** Background jobs: reorder, reservation expiry (if on), **invoice-generation retry**, outbound retries; status visible.
- **PLT-072 (P1A)** Optimistic concurrency on editable docs.
- **PLT-073 (P1A)** Document numbers per type (+ branch/fiscal per D-15); unique under concurrency; gaps allowed (§6.4.4).
- **PLT-074 (P1A)** Consistent errors: machine code + correlation id.
- **PLT-075 (P1A)** Referenced records deactivate, not hard-delete.
- **PLT-076 (P1A)** Paginated lists; API versioned.
- **PLT-077 (P2)** Attachments with limits + scope-inherited access.

**Retired:** PLT-001–006 multi-tenancy; Platform Operator.

### 5.2 Company and Master Data (MD)

- **MD-001 (P1A)** **Company singleton** profile: legal name, tax id, address, logo, base currency, timezone, fiscal year start, default language. Exactly one active company row; creation only at first-run migration/seed (not a tenant API).
- **MD-002 (P1A)** **Branches: seed three** (codes/names Q-3). Schema supports N branches; v1 seed = 3; not a hard-coded max (A-2). Warehouses require branch.
- **MD-003 (P1A)** Employee directory; optional user link.
- **MD-004 (P1A)** Admin user lifecycle + role/branch assignment; last admin protected.
- **MD-010 (P1A)** Product fields incl. **RequiresInspection**, reorder, lead time, default price, standard cost.
- **MD-011 (P1A)** Category hierarchy.
- **MD-012 (P1A)** UoM conversions; store base unit.
- **MD-013 (P1A)** Product statuses; no archive with on-hand > 0.
- **MD-014 (P2)** Lot/serial. **MD-015 (P2)** Multi-barcode. **MD-016 (P3)** Variants/images.
- **MD-020 (P1A)** Supplier + status (no multi-currency — D-12).
- **MD-021 (P2)** Supplier catalogue. **MD-022 (P2)** Performance score.
- **MD-030 (P1A)** Customer + credit limit + status.
- **MD-031 (P1A)** Addresses with defaults. **MD-032 (P2)** Groups.
- **MD-040 (P1A)** Warehouse + **branch id**.
- **MD-041 (P1A)** Locations; types **Receiving, Quarantine, Storage, Returns, Staging** (D-16).
- **MD-042 (P1A)** Block location; no deactivate while holding stock.
- **MD-043 (P3)** Capacity.
- **MD-050 (P1A)** Tax rates, payment terms, reason codes.
- **MD-051 (P3)** FX. **MD-060/061 (P2)** Import/duplicates.

**Rules:** deactivate not delete; Blocked supplier/customer rules; price changes never mutate existing docs.

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

### 5.9 Finance (FIN)

**Supplier invoices & matching (AP)**

- **FIN-001 (P1B)** Supplier invoice: supplier, invoice number, date, due date, lines, tax, total; links PO/receipts; branch from operational link.
- **FIN-002 (P1B)** Same supplier + invoice number unique.
- **FIN-003 (P1B)** **Three-way match:** invoice vs PO price vs accepted received qty within tolerances. *(TD-6: P1A may record invoice without full match UI; match required at P1B gate.)*
- **FIN-004 (P1B)** Statuses: Draft, Matched, Mismatch, Approved, Partially Paid, Paid, Disputed, Cancelled.
- **FIN-005 (P1B)** Mismatch cannot approve for payment without override+reason+permission.
- **FIN-006 (P1B)** Approval by amount via approval engine.
- **FIN-007 (P1B)** Supplier credit/debit notes linked; reduce payable.

**Supplier payments**

- **FIN-010 (P1B)** Payment: date, method, reference, amount, **RecordingBranchId** (rules: FIN-046…052).
- **FIN-011 (P1B)** Allocate across invoices; partial payments; unallocated remainder = supplier credit on the payment (FIN-048).
- **FIN-012 (P1B)** Payment cannot exceed outstanding unless excess explicitly kept as credit.
- **FIN-013 (P1B)** Duplicate-looking payment warning (supplier, amount, reference, date).
- **FIN-014 (P1B)** Never delete payment; reversal entry with reason.
- **FIN-015 (P2)** Threshold approval before release.

**Customer invoices (AR) — dispatched-only**

- **FIN-020 (P1B)** **Automatic customer invoice generation for each successful dispatch**, only quantities in that event; subsequent partial dispatches → **additional invoices** linked to the **same sales order**. Trigger = **dispatch only** in P1 (not confirmation, not delivery).
- **FIN-020a (P1B)** Generation **idempotent** w.r.t. dispatch: retries must not duplicate invoice lines/documents (§4.5.1; IDEM-07).
- **FIN-020b (P1B)** Quantities never dispatched are **never** invoiced.
- **FIN-021 (P1B)** Lines carry price, discount, tax from frozen SO lines; due date from payment terms; shipping charges optional.
- **FIN-022 (P1B)** Financial statuses: Draft, Issued, Partially Paid, Paid, Overdue (derived), Void.
- **FIN-023 (P1B)** Issued invoice immutable; corrections via credit notes.
- **FIN-024 (P2)** Invoice **PDF generation and email**. **P1:** invoice record + optional printable HTML; no PDF library required for gates.
- **FIN-025 (P1B)** Credit note refers to invoice, reason, partial/full, reduces receivable; caps §6.6.5.
- **FIN-026 (P1B)** Multiple invoices per SO (one per dispatch); payment allocation across them allowed.

**Customer payments**

- **FIN-030 (P1B)** Manual record or provider event; allocate; partial OK; overpayment → customer credit on payment.
- **FIN-031 (P2)** Payment link / online pay.
- **FIN-032 (P2)** Provider notifications signature-verified, idempotent by provider event id.
- **FIN-033 (P2)** Failed/refunded/disputed → visible reversals restoring balance.
- **FIN-034 (P2)** Reconciliation job provider vs recorded.
- **FIN-035 (P2)** Overdue reminders.

**Balances**

- **FIN-040 (P1B)** Balance per supplier/customer; aging: current, 1–30, 31–60, 61–90, 90+.
- **FIN-041 (P1A)** Outstanding/overdue exposure published to Sales via `ICreditExposureQuery` (MOD-11); **open-order component maintained atomically via `IReserveCredit`/`IReleaseCredit` (MOD-12…15)**.

**Tax / currency / cost**

- **FIN-050 (P1A)** Tax per line from configured rates; totals per §6.6.2.
- **FIN-051 (P3)** Foreign currency / FX (deferred).
- **FIN-060 (P2)** COGS from issue cost; gross margin.
- **FIN-061 (P3)** Landed cost.
- **FIN-070 (P3)** Export to external accounting.

**FIN business rules:** posted docs immutable; only dispatched qty invoiced; money ops accept idempotency keys; Finance records owed/paid — **no full GL**, no bank money movement.

#### 5.9.1 Dispatch vs invoice generation

- **FIN-027 (P1B)** Two distinct state axes:
  1. **Dispatch status** — owned by Fulfillment (FUL): `Dispatched` means stock issued and reservation consumed. **Does not depend on invoice success.**
  2. **Invoice generation status** — owned by Finance, per **dispatch id**: `NotStarted` → `Pending` → `Succeeded` | `Failed` | `Skipped` (Skipped only per FIN-029a).
- **FIN-028 (P1B)** Finance shall maintain exactly one `InvoiceGeneration` row per DispatchId; retries shall update that row and never create a duplicate invoice.

**Design note (FIN-028):** `Succeeded` stores `CustomerInvoiceId`. `InvoiceGenerationAttempt` may be a child history table keyed by `InvoiceGenerationId`; it has no DispatchId uniqueness.
- **FIN-029 (P1B)** **Invoice financial lifecycle** (`Draft/Issued/PartiallyPaid/Paid/Overdue/Void`) is **separate** from generation processing status. An invoice can be `Issued` while generation shows `Succeeded`; a `Failed` generation means **no invoice row** (or no issue) yet — stock remains dispatched.
- **FIN-029a (P1B)** **`Skipped` rules (safe default):**
  - **Closed list of system reasons:** only an explicitly configured, audited **non-billable policy** (e.g. configured line-level non-billable flag) may cause system-set `Skipped`. `Skipped` is **not** a casual escape hatch.
  - **Manual skip** is allowed only with permission **`finance.invoice.skip`** (permission catalogue, PLT-020).
  - Every skip requires a **reason code** and comment.
  - **Audit:** before/after status, actor/source, reason, correlation id, and dispatch id (PLT-040/043).
  - **Terminal by default:** `Skipped` cannot be reopened in P1; recovery = manual invoice path (TD-6) or approved correction. A `Failed` generation remains **retryable** (FIN-036/038).
  - **Reconciliation:** `Skipped` dispatches **appear in FIN-039 reconciliation and monitoring** with reason visible — never as silently healthy records.
  - **Dispatch-then-cancel (P1 policy — ADR-013):** a committed dispatch **remains billable** and is **not** automatically skipped merely because the SO is later cancelled; correction requires the approved credit-note/reversal process (SAL-052). *(PO override window: until B1 start.)*
- **FIN-036 (P1B)** **Retry:** outbox consumer + background job retry with backoff (INT-003). Poison failures → dead-letter + alert notification (PLT-050). Retries update the same `InvoiceGeneration` row (FIN-028).
- **FIN-037 (P1B)** **Monitoring:** dashboard/job view lists dispatches with `Pending/Failed`/`Skipped` generation older than threshold or with skip reason (NFR-030 extension).
- **FIN-038 (P1B)** **Recovery:** manual “retry invoice generation” command (permission `finance.invoice.generate`) with idempotency key = dispatch id (IDEM-07).
- **FIN-039 (P1B)** **Reconciliation job:** all `Dispatched` dispatches in window must have generation status `Succeeded` or `Skipped` (with reason, per FIN-029a) or open `Failed` incident; report mismatches (job P1B; full auto-heal P2).

#### 5.9.2 Cross-branch payments

- **FIN-045 (P1B)** **Invoice owning branch** = branch of the related sales order / operational document (immutable on invoice).
- **FIN-046 (P1B)** **Payment recording branch** = branch context of the user recording the payment (or company-wide finance user’s selected branch). Payment row stores `RecordingBranchId` (immutable fact).
- **FIN-047 (P1B)** **Allocation policy (temporary default, TD-8):**
  - **Every payment allocation requires `finance.payment.allocate` plus branch scope:** the caller must hold `finance.payment.allocate` **and** access the payment's **recording branch**. This applies to same-branch and cross-branch allocations alike.
  - **Same-branch allocation** (payment recording branch = invoice owning branch): additionally requires access to the invoice's owning branch (satisfied by the recording-branch scope when they are equal).
  - **Cross-branch allocation** (recording branch ≠ invoice owning branch): additionally requires **`finance.payment.cross_branch`**. **Access to both branches is not sufficient by itself** (TD-8/FIN-050b).
  - `finance.payment.record`, `finance.payment.allocate`, and `finance.payment.cross_branch` are **distinct** permissions; none implies another (permission catalogue, PLT-020).
  - **All cross-branch allocations are audited** (PLT-040/043: actor, payment id, invoice id, both branches, correlation id).
- **FIN-048 (P1B)** **Unallocated balance** lives on the **payment** (supplier/customer credit), not split per branch. Reporting shows unallocated by recording branch; consumption follows allocations to invoices of various branches.
- **FIN-049 (P1B)** Unauthorized cross-branch operations follow the established 404/403 policy: branch Accountant without `cross_branch` cannot allocate to foreign-branch invoices (403 per BRN-013/014 if invoice visible, 404 per BRN-012 if not).
- **FIN-050b (P1B)** **Q-11 (future change only):** the product owner may later allow multi-scope users to allocate cross-branch **without** `cross_branch`. **Current P1B behavior is unambiguous: cross_branch required (TD-8 / FIN-047).** Q-11 is a possible future relaxation, not an ambiguity in current behavior.
- **FIN-052 (P1B)** **Supplier AP mirror rules:** supplier payments use the same pattern with `RecordingBranchId` (mirrors FIN-045…049 on the AP side). *(Renumbered from duplicate FIN-051; FIN-051 is reserved for FX — see Tax/currency/cost.)*

**FIN business rules:** posted docs immutable; only dispatched qty invoiced; idempotent money ops; no full GL claim.

### 5.10 Dispatch and invoice processing status (Fulfillment)

Authoritative definitions of FUL-036…038 are stated here; Section 5.7 cross-references this section.

- **FUL-036 (P1B)** Dispatch status shall transition only from Draft to Dispatched; a committed dispatch shall not be reversed.
- **FUL-037 (P1B)** API responses for dispatch return dispatch status immediately; invoice generation is reported via **separate** status endpoint `GET .../dispatches/{id}/invoice-generation` (served by **Finance**, not by Fulfillment reading Finance tables) or embedded read-model field **labeled clearly** as non-authoritative for finance.
- **FUL-038 (P1B)** UI/API must not show “invoiced” solely because dispatch succeeded.

### 5.11 Reporting (RPT)

- **RPT-001 (P1B)** Reports from Reporting read models; show last-updated time.
- **RPT-002 (P1B)** Respect permissions and **branch scope**.
- **RPT-003 (P1B)** Filter period, warehouse, category, supplier, customer; drill to source docs within scope.
- **RPT-004 (P1B)** CSV export same scope; PDF export P2.
- **RPT-005 (P2)** Saved/scheduled reports.
- **RPT-006 (P1B)** Ops dashboard includes invoice-generation failure counts (FIN-037).
- **RPT-010 (P1B)** Thin executive dashboard: stock value, open PO/SO value, sales period, late shipments, overdue AR, AP due, open alerts. Gross margin P2.
- **RPT-020 (P1B)** Stock level/value by warehouse/category; movement history. Aging/slow/ABC P2.
- **RPT-030–041 (P2)** Sales and procurement suites (P1B may include open/late PO list if trivial).
- **RPT-050–060 (P2)** Fulfillment/shipping KPIs; detailed aging APIs (simple aging may exist via FIN-040).
- **KPI definitions** documented; compute P2 unless trivial in P1B dashboard.

### 5.12 AI (AI) — not P1

All **P2/P3**, non-blocking:

- **AI-001–AI-009 (P2)** Governing rules: permission inheritance, no sensitive writes, data citation, prompt-injection resistance, provider abstraction, graceful degrade, logging, company switch/quota, data minimisation.
- **AI-010–AI-014 (P2/P3)** Business assistant (read-only tools; drafts P2).
- **AI-020–AI-024 (P2/P3)** Demand forecasting.
- **AI-030–AI-031 (P3)** Procurement advisor.
- **AI-040–AI-042 (P2/P3)** Anomaly detection (explainable rules first).
- **AI-050–AI-052 (P2/P3)** Reorder suggestions (P1 = rule-based INV-060 only).
- **AI-070 (P3)** Quality backtests.

### 5.13 Integrations (INT)

Covered in §7 (INT-001…052 with stages).

---

## 6. Cross-cutting requirements

### 6.1 Global invariants

| ID | Invariant |
| --- | --- |
| INV-004 | Stock non-negative; available ≥ 0; no double-reserve |
| BRN-001–014 | Branch scope + 404/403 policy |
| FIN-020*, FIN-028 | ≤1 invoice generation per dispatch; dispatched qty only |
| FIN-027 | Dispatch success ≠ invoice generation success |
| PLT-041/044 | Audit immutable + redacted |
| MOD-4/5/9 | Ownership + acyclic deps + no Sales→Finance tables |
| MOD-3/TXN-* | Atomic state+outbox; hybrid sync/async (§4.5) |
| PLT-032 | No self-approval |
| INB-035/036/037 | Inspection qty invariants; no double inspect; reject ≠ return |
| INV-012/091 | Movements immutable; Returns guarded |
| WAC-01…11 | WAC behaviors |
| INV-NEG | No negative stock |

### 6.2 Location eligibility

| Location | On-hand | Available for new reservations | Notes |
| --- | --- | --- | --- |
| Storage | Yes | **Yes** if not blocked | Only eligible source |
| Staging | Yes | **No** | Reserved/dispatch-pending goods |
| Receiving | Yes | **No** | Awaiting putaway |
| Quarantine | Yes | **No** | Always blocked |
| Returns | Yes | **No** | Awaiting disposition |
| (Picking) | workflow state | n/a | not a location type |

Blocked-in-Storage is subtracted once. Quarantine/Returns/Receiving/Staging are excluded from eligible on-hand and are not subtracted again (INV-002). Picked-reserved quantity in Staging is tracked separately and is not part of `unpicked_reserved`.

### 6.3 Negative stock

- **INV-NEG (P1A)** Forbidden everywhere in P1.

---

### 6.4 Document lifecycle and numbering

#### 6.4.1 Lifecycle design template

For each governed document type, design and test:

1. Valid states  
2. Allowed transitions (from → to)  
3. Permission required per transition  
4. Preconditions (qty, branch scope, stock, match, approval)  
5. Side effects (movements, reservations, events, invoices, audit)  
6. Reversibility (void vs reverse vs credit note)  
7. Audit entries on every transition  
8. Concurrency: optimistic concurrency; stale transition → conflict  
9. Idempotency: transition commands accept keys where duplicate could double-post  

Full permission matrices produced at module design time (not optional).

#### 6.4.2 P1 document states

| Document | States | Notes |
| --- | --- | --- |
| Goods Receipt | Draft, Received, Under Inspection, Completed, Cancelled | Partial inspection: Completed when PendingQty=0 |
| Inspection line/session | Open, PartiallyInspected, Completed | Multiple sessions; `InspectionCompleted` carries `IsFinal` (INB-034) |
| Dispatch | Draft, Dispatched | Committed dispatch is irreversible; correction paths are separate (FUL-036) |
| Invoice generation | NotStarted, Pending, Succeeded, Failed, Skipped | One `InvoiceGeneration` row per dispatch; DispatchId unconditional unique; Skipped per FIN-029a |
| Customer Invoice | Draft, Issued, Partially Paid, Paid, Overdue, Void | Financial only |
| Customer Return | **P2 only** | Not in P1 states |

#### 6.4.3 Event and transaction cross-reference

Lifecycle side effects are specified authoritatively by the event catalog in Section 4.4 and the transaction-boundary table in Section 4.5. This subsection intentionally does not duplicate either catalog.

#### 6.4.4 Document numbering

- **PLT-073a (P1A)** Pattern per document type, example `PO-{yyyy}-{branch?}-{seq}` (branch/fiscal per D-15).
- **PLT-073b (P1A)** Uniqueness via DB unique constraint + concurrent-safe generation (ADR).
- **PLT-073c (P1A)** **Gaps allowed** on void/cancel or sequence reserve failure. **No gapless numbering promise** without legal requirement (none demonstrated).
- **PLT-073d (P1A)** Voided numbers not reused.
- **PLT-073e (P2)** Fiscal-year rollover configurable; default continue or reset per pattern.

---

### 6.5 Inventory and reservation cross-reference

The authoritative inventory quantity definitions, eligibility formula, reservation lifecycle, and movement rules are **INV-002…026**, **FUL-010…014**, and **INV-004** in Section 5. This subsection is navigation only; it does not restate or override those requirements.


### 6.6 Financial calculation rules

#### 6.6.1 Currency and precision

- **FIN-CALC-01 (P1A)** Single **company base currency** for all P1 documents and balances. Multi-currency deferred (D-12).
- **FIN-CALC-02 (P1A)** Money uses SQL `decimal` (exact numeric), **never float/double**.
- **FIN-CALC-03 (P1A)** Scale follows base currency minor unit. Demo default **A-1: USD, scale 2**. Other currency via Q-1 changes scale (0/2/3). Not a universal legal rule.
- **FIN-CALC-04 (P1A)** Quantity decimal scale default 4 in base unit (ADR may adjust).

#### 6.6.2 Pricing, discount, tax order

- **FIN-CALC-05 (P1A)** **Tax-exclusive** line prices in P1.
- **FIN-CALC-06 (P1A)** Per line: (1) unit price × qty → gross; (2) line discount → net; (3) tax on **net** → tax amount; (4) store net and tax separately; line total = net + tax for display.
- **FIN-CALC-07 (P1A)** Order-level discount: prefer line-level only in P1; order-level P2 if needed.
- **FIN-CALC-08 (P1A)** Document totals: `subtotal = Σ line net`; `tax_total = Σ line tax`; `grand_total = subtotal + tax_total` (+ non-tax charges if defined) = **sum of rounded line components** (no second rounding pass).

#### 6.6.3 Rounding

- **FIN-CALC-09 (P1A)** Round each monetary line component **half away from zero** to currency scale at line time; totals = Σ rounded components. *(Other rounding only if PO mandates — Q-2.)*
- **FIN-CALC-10 (P1A)** Percentages stored ~6 dp; not rounded before multiply; only money outputs round.

#### 6.6.4 Immutable snapshots

- **FIN-CALC-11 (P1A)** Posted financial docs freeze line amounts, tax, totals, currency — later master-data changes do not alter them.
- **FIN-CALC-12 (P1A)** SO prices frozen at confirm (SAL-024); invoice copies frozen values for dispatched qty.

#### 6.6.5 Credit notes and payment allocation caps

*(Consumed by P1B money operations — credit notes (FIN-025/FIN-007), payment allocation (FIN-011/FIN-030/FIN-052), supplier payments (FIN-012); tagged **P1B** since . General money rules needed earlier (currency, scale, tax, rounding, snapshots) stay P1A as FIN-CALC-01…12.)*

- **FIN-CALC-13 (P1B)** Credit notes against an invoice **≤ original invoice total**.
- **FIN-CALC-14 (P1B)** Payment allocations to an invoice **≤ invoice open balance** (total − payments − credits). Excess = unallocated credit on payment (not silent overpay on invoice).
- **FIN-CALC-15 (P1B)** Σ allocations **≤ payment amount**.
- **FIN-CALC-16 (P1B)** Supplier payment same caps on AP side.

#### 6.6.6 Explicit non-claims

- No double-entry GL, chart of accounts, or statutory reporting in P1.
- Tax compliance for any jurisdiction **unresolved** (Q-2); rates are configuration only.

---

### 6.7 Security and privacy

#### 6.7.1 Authentication

- **SEC-01 (P1A)** HTTPS/TLS everywhere.
- **SEC-02 (P1A)** Password hashing strong KDF (parameters ADR).
- **SEC-03 (P1A)** Refresh rotation + reuse detection + revocation (PLT-011).
- **SEC-04 (P1A)** Rate limiting on auth and sensitive endpoints.
- **SEC-05 (P1A)** Lockout after repeated failures.

#### 6.7.2 Authorization

- **SEC-10 (P1A)** Authentication ≠ authorization; every request re-authorizes.
- **SEC-11 (P1A)** Object-level, function-level, property-level, branch-level (BRN, PLT-020–024).
- **SEC-12 (P1A)** IDOR tests across modules (foreign branch docs, nested lines).
- **SEC-13 (P1A)** Documented **404/403 policy** = BRN-011…014; automated tests for 401/403/404 matrix.

#### 6.7.3 Secrets and data protection

- **SEC-20 (P1A)** Secrets from environment/secret store; never in repo.
- **SEC-21 (P1A)** Integration credentials encrypted at rest; never logged/returned in full.
- **SEC-22 (P1A)** Sensitive data redaction in logs (tokens, passwords, full PANs, excess PII).
- **SEC-23 (P1A)** Errors do not leak stack traces or secrets.
- **SEC-24 (P1A)** Audit and log redaction per PLT-044.
- **SEC-25 (P1B)** Invoice-generation retry endpoints: permission + idempotency (FIN-038).

#### 6.7.4 Audit integrity and retention

- **SEC-30 (P1A)** Audit write path separate from business edits; no update/delete API for audit rows.
- **SEC-31 (P1A)** Retention configurable; duration open (Q-8); export P2.

#### 6.7.5 Files

- **SEC-40 (P1A)** P1 file handling shall support authenticated company-logo upload and non-public retrieval with type and size limits.

#### 6.7.6 Webhooks

- **SEC-50 (P2)** Inbound webhook signature + timestamp/replay window (INT-005). (No P1 inbound webhook source exists; activates with the first P2 webhook source per ADR-011.)
- **SEC-51 (P2)** Outbound webhook signing secrets.

#### 6.7.7 Privacy

- **SEC-60 (P1A)** Collect only needed personal data; no special-category data required.
- **SEC-61 (P2)** Export/delete per retention policy (NFR-005).

---

## 7. External integrations

| Integration | Stage stance |
| --- | --- |
| Email | P1A: adapter + fake; real send if configured (TD-2); manual mark-sent fallback |
| Carrier | P1A: manual path full; automated adapter P2 |
| Payment provider | P2; P1B manual payment entry + allocation |
| AI providers | Not P1 |
| Outbound webhooks | P2 |
| Client API | API keys + scopes P1A (INT-050) |

- **INT-001 (P1A)** Adapter interface + fake per external category used.
- **INT-002 (P1A)** Outbound from queued jobs fed by outbox; **never inside DB transaction**.
- **INT-003 (P1A)** Retry exponential backoff + jitter; dead-letter visible.
- **INT-004 (P1A)** Timeouts; simple circuit breaker OK P1.
- **INT-005 (P2)** Webhooks: signature, replay window, store raw first, once per provider event id. (No P1 inbound webhook source exists; see ADR-011.)
- **INT-006 (P1A)** Integration log: status, duration, correlation id, redacted payload.
- **INT-007 (P1A)** Credentials encrypted; never in API/logs.
- **INT-008 (P1A)** Health view: last success, failures, queue depth.
- **INT-009 (P1A)** Respect provider rate limits.
- **INT-010–013 (P2)** Payment provider link/events/refunds/reconciliation.
- **INT-020–022 (P2)** Carrier adapters; P1 manual carrier works without integration.
- **INT-030–031 (P1A)** Email queue when configured; bounce flag P2.
- **INT-040–041 (P2/P3)** AI provider abstraction.
- **INT-050 (P1A)** API keys with scopes; revoke. **Home: A0 (ADR-009).**
- **INT-051 (P2)** Outbound webhooks.
- **INT-052 (P1A)** OpenAPI from code.

Failure philosophy: business commit never depends on external success; effects eventually run or surface as failed jobs.

---

## 8. Non-functional requirements

### 8.1 Technology baseline

The fixed stack and excluded technologies are defined once in Section 3.1; this section does not restate them.


### 8.2 Security

- **NFR-001 (P1A)** TLS; secrets not in repo.
- **NFR-002 (P1A)** OWASP API Top 10 basics: validation, object auth, rate limits, over-posting protection.
- **NFR-003 (P1A)** Encrypted integration secrets; logs redacted.
- **NFR-004 (P2)** Dependency vulnerability scanning in CI.
- **NFR-005 (P2)** Export/delete company data per retention policy.

### 8.3 Data integrity and concurrency

- **NFR-010 (P1A)** Stock/money changes in transactions including outbox when required.
- **NFR-011 (P1B)** Automated concurrency tests: no over-reservation; no double payment application; **no double invoice per dispatch (single `InvoiceGeneration` row, unique DispatchId)**; **no double credit reserve (MOD-12/13)**.
- **NFR-012 (P1A)** Critical invariants also DB-constrained where practical (unique keys, FKs, non-negative checks).
- **NFR-013 (P1A)** Versioned migrations; controlled apply.

### 8.4 Performance targets

- **NFR-020 (P1B)** 95% single-record reads < 300 ms; lists < 800 ms on W1 dataset (target).
- **NFR-021 (P1B)** Availability check < 300 ms (W2).
- **NFR-022 (P1B)** Dashboards < 2 s on read models (W5).
- **NFR-023 (P1A)** Paginated lists; indexes for common filters.
- **NFR-024 (P2)** Repeatable load-test harness beyond W1–W5.
- **NFR-025 (P1B)** **Measurable workloads:**
  - **W1 Read:** 100k products, 1M movements, 50k SOs — p95 GET < 300 ms; p95 list page < 800 ms.
  - **W2 Availability:** 50 parallel availability checks — p95 < 300 ms on W1 data.
  - **W3 Reserve:** 100 parallel confirms on contested SKU — **0 over-reserve**; p95 confirm < 500 ms.
  - **W4 Dispatch:** 50 parallel dispatches — stock correct; invoice gen eventually Succeeded **≤ 1 per dispatch**.
  - **W5 Dashboard:** thin exec dashboard p95 < 2 s on read models.
  Seed counts from data generator (NFR-052); targets confirmable.

### 8.5 Reliability and operations

- **NFR-030 (P1A)** Health: app, database, job runner; **P1B:** invoice-gen queue depth + failed count.
- **NFR-031 (P1A)** Structured logs with correlation id, user, branch.
- **NFR-032 (P1A)** Background jobs resume without loss/duplication (idempotent handlers).
- **NFR-033 (P2)** Metrics/tracing; documented backup/restore.

### 8.6 Maintainability

- **NFR-040 (P1A)** CI shall run architecture tests that enforce all module, contract, table-ownership, shared-infrastructure, and event-dependency rules in MOD-1…5.

**Design note (NFR-040):** Tests cover the acyclic module C/R graph, leaf contract graph, shared-infrastructure interface-only writes, prohibition on cross-module domain-table access, and non-compile-time event subscriptions.
- **NFR-041 (P1A)** Unit + integration tests per module.
- **NFR-042 (P1A)** Formatting/analyzers in CI.
- **NFR-043 (P1A)** **Required ADRs before/at implementation** *  
  1. Inventory concurrency (D-14) *(due before A1 code)*  
  2. WAC algorithm, **cost-pool membership**, and reversal (WAC-05/WAC-07/WAC-11/TD-5) — must define: **which StockMovement types change the cost pool** (internal putaway = location move, no pool change; same-warehouse location transfer = no pool change; cross-warehouse transfer = TransferOut leaves source pool / TransferIn enters destination pool at source cost — one economic movement, not two independent pool entries; reversals = remove the original pool entry at **original movement cost**, never current WAC); and state explicitly that **not every StockMovement record is an independent cost-pool entry** (double-counting prevention, esp. TransferOut+TransferIn pairs and putaway). **ADR only — no algorithm implemented in the PRD.** *(due before A1 costing code)*  
  3. Contract and event assembly layout (leaf contracts; §4.1/4.2) — **DONE: ADR-001**  
  4. Event consumer-owned DTO / serialization strategy (versioned payloads, OUT-05) — **DONE: ADR-002**  
  5. **Ambient transaction mechanism** — must explain: transaction owner/orchestrator; per-module DbContext strategy; how multiple module operations share one SQL Server transaction; how shared-infrastructure tables are accessed (MOD-1/MOD-1a); how MOD-1 ownership remains enforceable; how rollback and outbox atomicity are tested; how integration tests reproduce production transaction behavior — **DONE: ADR-003**  
  6. Dispatch/invoice orchestration and generation-row claim (D-22, TD-10, FIN-028) *(due before A4/B1)*  
  7. **Atomic credit reservation contract** (MOD-12…15) — must explain: atomic reserve/release semantics; concurrency strategy; idempotency; cancellation and order-reduction behavior; **dispatch/invoice conversion behavior (proportional `IReleaseCredit` at invoice issuance — MOD-15)**; failure-closed behavior (TD-3); exposure reconciliation *(due before A3)*  
  8. **Inspection session concurrency** and duplicate-inspection prevention (INB-036) *(due before A2 inspection code)*  
  9. Document numbering (D-15) — **DONE: ADR-005**  
  10. Putaway *(due before A2)*  
  11. Currency (D-19/D-12, Q-1) — **DONE: ADR-014** (locale residual = Q-1, before B2)
- **NFR-044 (P1A)** API versioning policy — **decided: URL segment `/api/v1`, additive evolution within v1, breaking → `/api/v2` (ADR-006).**
- **NFR-045 (P1A)** The API shall follow ADR-006 for versioning, routes, paging, errors, concurrency, idempotency, and authentication.

### 8.7 Testing

- **NFR-050 (P1A)** Layers: unit, integration on real SQL Server container, API permission tests, architecture tests, concurrency tests.
- **NFR-051 (P1A)** Each P1 requirement → test or documented waiver.
- **NFR-052 (P1A)** Demo/load data generator (minimal OK for W1 seed).

### 8.8 Delivery

- **NFR-060 (P1A)** One Docker Compose path: app + SQL Server (+ fakes).
- **NFR-061 (P1A)** CI: build, test, format, container build on PR.
- **NFR-062 (P1A)** README, architecture overview, runbook, generated OpenAPI.
- **NFR-070 (P1A)** Timestamps UTC; display company timezone.
- **NFR-071 (P2)** Localization (English first).

---

## 9. Data requirements

### 9.1 Data rules

- **DM-1 (P1A)** `BranchId` on branch-owned tables; **Company** is singleton without multi-tenant `CompanyId` mandate; no speculative tenancy columns (D-2).
- **DM-2 (P1A)** Money decimal per base currency scale; qty decimal base unit (scale 4 default).
- **DM-3 (P1A)** Surrogate keys (**BIGINT**); human numbers displayed.
- **DM-4 (P1A)** No cross-schema FKs; store other module ids + snapshots.
- **DM-5 (P1A)** Explicit status enums stored as **string status codes**; transitions tested.
- **DM-6 (P1A)** History via audit/status/immutable ledgers.
- **DM-7 (P1A)** Optimistic concurrency; rowversion alone ≠ stock concurrency solution (INV-021/D-14).
- **DM-8 (P1A)** Finance tables never referenced from Sales (MOD-9); contract DTOs live in **`Finance.Contracts` — a per-module leaf contract assembly (ADR-001; no shared contracts assembly)** — **interface owned by Finance**.

### 9.2 Main entities

| Module | Entities |
| --- | --- |
| Platform Core | **User, Role, Permission, UserRole, RolePermission, UserBranchScope, UserWarehouseScope, RefreshToken, ApiKey, ApprovalPolicy, ApprovalPolicyStep, ApprovalRequest, ApprovalDecision, AuditLog, Notification, DocumentSequence, IdempotencyKey, JobDefinition/JobRun** |
| Master Data | **Company (singleton), Branch, Employee, Product, ProductCategory, UnitOfMeasure, UnitConversion, Supplier, SupplierContact, SupplierAddress, Customer, CustomerContact, CustomerAddress, Warehouse, StorageLocation, TaxRate, PaymentTerm, ReasonCode** |
| Inventory | **StockBalance, StockMovement, Reservation, Transfer, TransferLine, Adjustment, AdjustmentLine, ProductCost, IncomingProjection (per product/warehouse open-PO qty — INV-063)**, Lot/Serial (P2), StockCount (P2) |
| Procurement | **PurchaseRequisition, RequisitionLine, QuotationRequest, QuotationRequestLine, QuotationRequestSupplier, SupplierQuote, SupplierQuoteLine, QuoteAward, PurchaseOrder, PurchaseOrderLine, PurchaseOrderRevision**; Agreement* (P2) |
| Inbound | **ExpectedDelivery, ExpectedDeliveryLine, DeliveryRegistration, GoodsReceipt, GoodsReceiptLine, Inspection, InspectionSession, InspectionLine, Discrepancy, SupplierReturn, SupplierReturnLine**; InspectionPlan* (P2) |
| Sales | **SalesOrder, SalesOrderLine, Backorder**; Quotation*, PriceList*, CustomerGroup* (P2), CustomerReturn* (P2) |
| Fulfillment | **FulfillmentOrder, FulfillmentOrderLine, PickList, PickListLine, Package, PackageItem, Dispatch, DispatchLine** |
| Shipping | **Carrier, Shipment, ShipmentPackage, TrackingEvent, DeliveryConfirmation**; RateTable* (P2) |
| Finance | **SupplierInvoice, SupplierInvoiceLine, InvoiceMatchResult, SupplierPayment (RecordingBranchId), SupplierPaymentAllocation, CustomerInvoice, CustomerInvoiceLine, CreditNote, CreditNoteLine, DebitNote, CustomerPayment, CustomerPaymentAllocation, PaymentReversal, **CustomerCreditExposure (authoritative credit-exposure record — MOD-12), CustomerBalance, SupplierBalance (FIN-040 projections)**, InvoiceGeneration (DispatchId unconditional unique; Status NotStarted/Pending/Succeeded/Failed/Skipped; CustomerInvoiceId nullable; AttemptCount; LastAttemptAt; LastSucceededAt; LastErrorCode; LastErrorMessageRedacted; NextRetryAt; CreatedAt; UpdatedAt; RowVersion; CorrelationId), InvoiceGenerationAttempt (optional retry history: Id, InvoiceGenerationId, AttemptNumber, StartedAt, CompletedAt, Result, RedactedErrorCode, CorrelationId — no DispatchId uniqueness)** |
| Reporting | **StockSnapshot, SalesFact, KpiSnapshot, DashboardLayout**, other facts P2 |
| Integrations | **OutboxMessage, InboxMessage/WebhookEvent, IntegrationLog, IntegrationConnection/Credential** |

Also **DM-8:** Finance contract interfaces (`ICreditExposureQuery` etc.) owned by Finance; Sales references contracts only — never Finance tables.

Table counts are estimates for design, not feature targets (G-8).

---

## 10. Testing and acceptance

### 10.1 Acceptance themes

| Theme | Stage | Acceptance sketch |
| --- | --- | --- |
| Stock never negative | P1A | Concurrent ops; no negative balances |
| No double reservation | P1A | Parallel confirms; unit once |
| **Atomic credit reservation** | P1A | Two concurrent confirms compete for remaining credit → exactly one succeeds; cancellation releases credit; duplicate confirm does not double-reserve; outbox lag does not permit credit overrun |
| Branch isolation + **404/403 policy** | P1A | Foreign branch → 404; in-scope wrong function → 403; unauth → 401 |
| IDOR nested | P1A | Lines/packages fail closed |
| Receiving paths | P1A | Inspection vs no-inspection + putaway |
| **Partial inspection** | P1A | Two sessions; PendingQty math; double-inspect rejected; reject without auto SR; consumers branch on `IsFinal` (partial ≠ final) |
| **Returns guard** | P1A | CustomerReturn movement without P2 doc rejected |
| WAC behaviors | P1A | Opening, receipt blend, transfer, **reversal after later receipt**, **reversal after partial issue**, reversal to zero (WAC-08), decimal qty; no negative average; pool≡ StockValue |
| Partial dispatch invoicing | P1B | 2+2 qty; never 5; ≤1 gen per dispatch (unique InvoiceGeneration row) |
| **Dispatch ≠ invoice gen** | P1B | Force gen failure → dispatch stays Dispatched; retry updates same row and succeeds once; no second invoice |
| **Skipped generation** | P1B | Only closed-list reasons; manual skip needs `finance.invoice.skip` + reason + audit; appears in reconciliation; dispatch-then-cancel stays billable (P1 policy — ADR-013) |
| Three-way match | P1B | Match/mismatch/override |
| Payment record + allocation caps | P1B | FIN-CALC-14/15 caps; idempotent replay |
| **Cross-branch allocation** | P1B | `finance.payment.allocate` + recording-branch scope required for **every** allocation; cross-branch additionally requires `finance.payment.cross_branch`; both-branch scope alone insufficient; audit entry present; 404/403 matrix |
| Credit contract | P1A | Sales uses reserve contract; architecture test no Sales→Finance tables |
| Hybrid TXN | P1A | Rollback GR removes outbox; external fake failure doesn’t hold TXN |
| Outbox atomicity | P1A | Domain row + outbox row commit together; rollback leaves no orphan message; consumer dedupe by event id |
| Audit before/after + redaction | P1A | Sensitive fields redacted in audit API |
| Authorization coverage | P1A | All endpoints have permission metadata |
| Performance workloads | P1B | W1–W5 measured (targets A-3) |

### 10.2 Traceability

Requirement IDs appear in test names (NFR-051). Acceptance for a requirement is its own statement, verified by tests named with its ID; §10.1 lists the cross-cutting scenarios. Detailed Given/When/Then criteria are written per slice before that slice starts. The companion traceability matrix records each requirement's current identifier, priority, gate, test-reference placeholder, related ADR, and any identifier mapping.

---

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

## 12. Decision and ADR index

| ID | Decision | Options | Status |
| --- | --- | --- | --- |
| D-1 | Frontend | API only; thin UI; full app | API-first; optional thin UI in hardening |
| D-2 | Company data model | Multi-tenant; singleton company + BranchId; schema/company | **Singleton Company config + BranchId; no speculative CompanyId (corrected)** |
| D-3 | Module communication | In-process+outbox; direct only; broker | **Hybrid sync R + async outbox E; no broker (expanded §4.5)** |
| D-4 | When stock leaves on-hand | Pick; dispatch | **Dispatch** — referenced by FUL-031 |
| D-5 | Customer invoice trigger | Confirm; dispatch; delivery | **Dispatch only, per-dispatch invoices** |
| D-6 | External providers | Fakes only; fakes+real | Fakes P1; optional real later |
| D-7 | Forecasting | Stats; ML; LLM | P2+ stats (not P1) |
| D-8 | AI data exposure | Full; aggregates; none | Aggregates/minimal when AI exists |
| D-9 | Costing | WAC; FIFO | **WAC P1** (scope WAC-01); FIFO P3 |
| D-10 | Approvals | Fixed; configurable | Configurable; seed two levels |
| D-11 | Lot/serial | P1; P2 | **P2** |
| D-12 | Currencies | Multi; single | **Single base currency P1** |
| D-13 | Auth | Built-in; IdP | Built-in P1; IdP P3 |
| D-14 | Stock concurrency | Various SQL strategies | **Invariants+tests in PRD; SQL Server strategy in Inventory ADR** |
| D-15 | Numbering scope | Global; branch; fiscal | Per type + optional branch/fiscal; gaps allowed |
| D-16 | Location types | With/without Picking | **No Picking type** |
| D-17 | RFQ | P1; P2 | **P1 simplified** |
| D-18 | AI phases | P1; P2/P3 | **P2/P3 only** |
| D-19 | Base currency demo | USD 2dp; other | **A-1 until Q-1** |
| D-20 | SF.png authority | Binding; conceptual | **Conceptual; Multi-Tenancy superseded** |
| D-21 | Sales–Finance dependency | Cycle; tables; contracts | **Acyclic: Sales→Finance query contracts; Finance→Sales events only (MOD-5/9/11)** |
| D-22 | Dispatch vs invoice coupling | Same TXN; same status; separate | **Separate TXNs + separate statuses (FIN-027)** |
| D-23 | Branch count | Hard-coded 3; seed 3 | **Seed three; not schema max (MD-002)** |
| D-24 | 404 vs 403 | Always 403; always 404; hybrid | **Hybrid BRN-011…014** |
| D-25 | Invoice PDF | P1; P2 | **P2 (FIN-024)** |

*Mechanism-level decisions are recorded in `ADR/ADR-001` through `ADR/ADR-019`; see `docs/Decision Register.md` for decision tracking.*

---

### 12.1 References

- `ADR/ADR-001…019` — architecture decisions (authoritative for mechanism-level choices).  
- `SF.png` conceptual only (Appendix B).  
- Stack: .NET 10, ASP.NET Core, EF Core 10, SQL Server, xUnit, Docker Compose, GitHub Actions, OpenAPI.

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

## Appendix A. Glossary

| Term | Meaning |
|---|---|
| Available | Eligible Storage on-hand less unpicked reservations and blocked eligible stock (INV-002). |
| Branch scope | The branches and warehouses a principal is authorized to access (BRN-001…010). |
| C/R dependency | Compile-time contract reference and/or in-process runtime command/query dependency (Section 4.1). |
| E subscription | Asynchronous event consumption through the outbox; not a compile-time dependency (Section 4.1). |
| Incoming | Open purchase-order quantity not yet received; planning signal, not stock (INV-002/063). |
| On-hand | Physical quantity in warehouse locations (INV-002). |
| Outbox | Durable post-commit delivery record written atomically with the owning business transaction (OUT-01…07). |
| Picked-reserved | Reserved quantity moved to Staging and awaiting dispatch or release (INV-002/024/026). |
| Unpicked-reserved | Reserved quantity still in eligible Storage and subtracted from eligible on-hand (INV-002). |
| WAC | Weighted-average cost, scoped by product and warehouse (WAC-01…11). |



## Appendix B. Conceptual diagram alignment

Authority: conceptual only (D-20). **Not modified.**

### B.1 Aligned

Master data, procurement path, inbound inspect accept/reject, inventory hub, sales→reserve, fulfill pick/pack/dispatch, shipping track/confirm, finance AP/AR, foundation (auth, audit, notifications, integrations — **minus Multi-Tenancy**), analytics/AI band.

### B.2 Differences

| --- | --- | --- | --- |
| 1 | Multi-Tenancy | In foundation | **Superseded**; Company singleton |
| 2 | Branches | Absent | Seed three + auth |
| 3 | AI band | Equal prominence | P2/P3 |
| 4 | Partial inspection | Binary accept/reject | Sessions + PendingQty |
| 5 | Invoice vs dispatch | Not shown | Split statuses |
| 6 | Customer returns | Not shown | P2 workflow; Returns location P1 guarded |

### B.3 Possible future image update

Replace Multi-Tenancy with “Single Company · Three Branches”; optional AI “Phase 2” badge.

---

## Appendix C. Grouped requirement-ID aliases

The source defines the following ranges as grouped scopes rather than individual atomic statements. Each ID is retained as an explicit traceability alias; the specification does not invent a per-ID decomposition. The owner should decompose these groups before implementing their scope.

| Requirement IDs | Priority | Grouped scope |
|---|---|---|
| **MD-060** | P2 | Master-data import capability; the source does not specify a per-entity import breakdown. |
| **MD-061** | P2 | Master-data duplicate detection capability; the source does not specify matching fields or merge rules. |
| **INT-030** | P1A | Email queue when configured; manual mark-sent fallback remains available when email is not configured. |
| **INT-031** | P2 | Bounce flag for email delivery. |
| **AI-001…009** | P2 | Governing AI rules: permission inheritance, no sensitive writes, data citation, prompt-injection resistance, provider abstraction, graceful degradation, logging, company switch/quota, and data minimisation. |
| **AI-010…014** | P2/P3 | Business assistant capability with read-only tools; draft creation is P2. Per-ID decomposition is not specified. |
| **AI-020…024** | P2/P3 | Demand forecasting capability. Per-ID decomposition is not specified. |
| **AI-030…031** | P3 | Procurement advisor capability. Per-ID decomposition is not specified. |
| **AI-040…042** | P2/P3 | Anomaly detection, with explainable rules first. Per-ID decomposition is not specified. |
| **AI-050…052** | P2/P3 | Reorder suggestions; P1 remains rule-based through INV-060 only. Per-ID decomposition is not specified. |
| **INT-010…013** | P2 | Payment-provider links, events, refunds, and reconciliation. Per-ID decomposition is not specified. |
| **INT-020…022** | P2 | Carrier adapters; the P1 manual carrier path works without an integration. Per-ID decomposition is not specified. |
| **INT-040…041** | P2/P3 | AI provider abstraction. Per-ID decomposition is not specified. |
| **RPT-030…041** | P2 | Sales and procurement reporting suites. Per-ID decomposition is not specified. |
| **RPT-050…060** | P2 | Fulfillment/shipping KPIs and detailed aging APIs. Per-ID decomposition is not specified. |
