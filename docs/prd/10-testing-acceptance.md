<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §10 · nothing changed · navigation: docs/prd/INDEX.md -->

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
