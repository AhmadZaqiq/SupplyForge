<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §2.1–2.5 · nothing changed · navigation: docs/prd/INDEX.md -->

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
