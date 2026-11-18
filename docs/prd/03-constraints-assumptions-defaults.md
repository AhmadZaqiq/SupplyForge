<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §3 · nothing changed · navigation: docs/prd/INDEX.md -->

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
