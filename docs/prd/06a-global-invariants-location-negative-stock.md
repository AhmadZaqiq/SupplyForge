<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §6.1–6.3 · nothing changed · navigation: docs/prd/INDEX.md -->

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
