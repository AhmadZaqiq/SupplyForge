<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §4.5 · nothing changed · navigation: docs/prd/INDEX.md -->

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
