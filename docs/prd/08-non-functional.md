<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §8 · nothing changed · navigation: docs/prd/INDEX.md -->

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
