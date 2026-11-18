<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §2.6–2.7 · nothing changed · navigation: docs/prd/INDEX.md -->

### 2.6 Users, roles, and permissions

#### 2.6.1 Roles

| Role | Main responsibilities | Must not |
| --- | --- | --- |
| Company Executive | Cross-branch dashboards, high-threshold approvals | Change stock; edit posted docs |
| Branch Manager | Assigned-branch ops oversight and approvals | Self-approve; edit posted financials |
| Procurement Manager | Approve req/PO; company supplier master | Receive goods; self-approve |
| Procurement Officer | Req, RFQ, quotes, draft PO | Approve PO; post invoices |
| Warehouse Manager | Warehouses/locations in scope; approve transfers/adjustments | Create PO/invoices |
| Warehouse Operator | Receive, putaway, pick, pack, dispatch | Change prices; approve adjustments |
| Quality Inspector | Inspect accept/reject | Edit POs |
| Sales Representative | Customers, SOs, availability in branch | Edit stock; change suppliers |
| Accountant | Invoices, payments, matching (company or branch finance scope) | Change stock/order qty |
| Company Administrator | Users, roles, settings, branch assignment | Bypass audit; edit posted financials |
| Integration Client | Scoped API credentials | Act outside scopes |

No Platform Operator; no multi-company admin surface.

#### 2.6.2 Access principles

- **PLT-ROLE-1 (P1A)** Fine-grained permission catalogue; roles = bundles; admin customizable within catalogue.
- **PLT-ROLE-2 (P1A)** Separation of duties: requester ≠ approver; payment recorder ≠ invoice approver above threshold.
- **PLT-ROLE-3 (P1A)** Branch (and optional warehouse) scope; out-of-scope data invisible and unwritable.
- **PLT-ROLE-4 (P1A)** Company-wide permissions separate from branch operational permissions.
- **PLT-ROLE-5 (P1A)** Scope/permission changes effective within configurable delay (default 5 min) or on token refresh (ADR).
- **PLT-ROLE-6 (P2)** 2FA; IdP P3.

---

### 2.7 Company and branch model

#### 2.7.1 Model

- **Company is a singleton configuration record** (one row / one bounded context instance): legal profile, base currency, timezone, fiscal year, settings. It is **not a tenant**, not provisionable, not multi-instance in v1 (D-2, A-4).
- **Three branches are initial seed data**, not a hard-coded technical maximum (MD-002, A-2). The schema allows additional branches via configuration if the product owner later adds them; v1 seed and acceptance use exactly three.
- Warehouses belong to branches; locations belong to warehouses.
- Branch-owned documents carry `BranchId`: PO, GR, inspection, supplier return, SO, fulfillment, shipment, transfer, adjustment, counts, AP/AR invoices, payments, credit notes (as applicable).
- Company-shared master data has no `BranchId`: products, categories, UoM, suppliers, customers, tax rates, payment terms, reason codes, employees, users, **Company** singleton.

#### 2.7.2 Shared vs branch-specific

| Shared (company-wide) | Branch-specific |
| --- | --- |
| Products, categories, units, barcodes | Warehouses, locations |
| Suppliers, customers, contacts | Stock balances, movements, reservations |
| Tax rates, payment terms, reason codes | Req, RFQ (destination), PO, GR, inspection, supplier returns |
| Users, roles, permission catalogue | SO, fulfillment, shipments |
| **Company** singleton (currency, fiscal year) | Transfers, adjustments, counts |
| Document type templates | AP/AR docs & payments (see §5.9.2 for payment recording branch) |
| Default price lists | Document sequences (may be per branch — D-15) |

#### 2.7.3 Authorization scope

- **BRN-001 (P1A)** Branch-owned records readable/writable only within caller’s branch scope unless company-wide permission for that action.
- **BRN-002 (P1A)** Enforced on reads, writes, searches, list filters, reports, exports, nested IDs, background jobs.
- **BRN-003 (P1A)** Object-level: load-by-id verifies ownership before return/mutation. **ID bypass forbidden.**
- **BRN-004 (P1A)** Function-level: endpoint declares permissions; deny by default.
- **BRN-005 (P1A)** Property-level: payment instrument details and cross-branch aggregates gated.
- **BRN-006 (P1A)** Lists/reports filter by effective scope; cross-branch views need explicit permission + audit.
- **BRN-007 (P1A)** Background jobs re-apply scope for user-visible outputs; no privilege escalation from a branch user context.
- **BRN-008 (P1A)** Exports use the same filters/field limits as interactive queries.
- **BRN-009 (P1A)** Last active Company Administrator cannot be deactivated.
- **BRN-010 (P1A)** Users assigned one+ branches or company-wide scope flag.

**HTTP status policy (P1A) — consistent 404/403:**

- **BRN-011 (P1A)** If the resource **does not exist** → **404**.
- **BRN-012 (P1A)** The API shall return 404 for branch-isolated resources outside caller scope, except when diagnostic foreign-branch permission requires 403.
- **BRN-013 (P1A)** If the resource is **in scope** but the caller lacks the **function permission** for the action → **403**.
- **BRN-014 (P1A)** Authenticated but insufficient role for a **known in-scope** document action → **403**; unauthenticated → **401**.
- Rationale: branch isolation prefers non-disclosure (404); functional denial on visible resources is explicit (403). Tests cover both (§10).

#### 2.7.4 Cross-branch transfers

- **BRN-020 (P1A)** Inter-warehouse transfer (same or different branch). Different branches = inter-branch.
- **BRN-021 (P1A)** Inter-branch requires approval on both branches or company-wide approver (configurable; default both-or-company).
- **BRN-022 (P1A)** In-transit not available in either branch.
- **BRN-023 (P1A)** Destination receive + putaway per §5.5.
- **BRN-024 (P2)** Discrepancy via adjustment with dual-branch audit visibility.

#### 2.7.5 Business rules

- No create/update outside scope; scope changes do not rewrite history; branches deactivated not deleted while holding stock/open docs.

---
