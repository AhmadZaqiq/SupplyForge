<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §2.8 · nothing changed · navigation: docs/prd/INDEX.md -->

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
