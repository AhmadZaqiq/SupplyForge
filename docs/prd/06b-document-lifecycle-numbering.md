<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §6.4–6.5 · nothing changed · navigation: docs/prd/INDEX.md -->

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

