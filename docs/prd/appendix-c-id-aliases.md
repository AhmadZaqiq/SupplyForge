<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of Appendix C · nothing changed · navigation: docs/prd/INDEX.md -->

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
