<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5.9 · nothing changed · navigation: docs/prd/INDEX.md -->

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
