<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §5 intro + §5.1 · nothing changed · navigation: docs/prd/INDEX.md -->

## 5. Functional requirements

### 5.1 Platform Core (PLT)

**Identity**

- **PLT-010 (P1A)** Users admin-created (invite optional); email+password; salted strong KDF hashes (ADR).
- **PLT-011 (P1A)** Access + refresh tokens; refresh **rotated**; revoked on logout/password change/deactivation/reuse detection.
- **PLT-012 (P1A)** Lockout + rate limit on auth.
- **PLT-013 (P1A)** Password reset expiring single-use link; admin reset fallback (Q-5).
- **PLT-014 (P2)** 2FA. **PLT-015 (P3)** External IdP.

**Authorization**

- **PLT-020 (P1A)** Permission catalogue; role bundles; multi-role union.
- **PLT-021 (P1A)** Branch/warehouse scope (BRN).
- **PLT-022 (P1A)** Endpoint permission metadata; deny default; coverage tests.
- **PLT-023 (P1A)** Record-level rules on top of permissions.
- **PLT-024 (P1A)** Nested resources authorize via parent scope.

**Approvals**

- **PLT-030 (P1A)** Policies by document type: amount, category, branch; ordered steps.
- **PLT-031 (P1A)** Approve/reject/return + comment; recorded.
- **PLT-032 (P1A)** No self-approval; no double-step by same person.
- **PLT-033 (P2)** Delegation/escalation.
- **PLT-034 (P1A)** Engine for req, PO, adjustment, large discount, credit override, supplier payment thresholds.

**Audit**

- **PLT-040 (P1A)** Create/update/void/status/permission/sign-in with user, time, entity, **before/after values**, correlation id, source.
- **PLT-041 (P1A)** Immutable via app API; searchable (entity, user, action, date, branch).
- **PLT-042 (P2)** Export/retention/integrity; duration Q-8.

**Audit capture & redaction**

- **PLT-043 (P1A)** Before/after capture: for updates, store changed-field map (field → old, new); for creates, new values; for status changes, status old/new + reason. Full-row snapshots optional P2.
- **PLT-044 (P1A)** **Sensitive-field redaction** in audit payloads and logs: passwords, token hashes, webhook secrets, API key secrets, full payment instrument numbers (keep last4), national ids if ever stored. Redaction list configured in code + tests.
- **PLT-045 (P1A)** Audit query API respects BRN scope (company-wide permission for cross-branch audit).

**Notifications**

- **PLT-050 (P1A)** In-app notifications: approval requested, low stock, delayed shipment, payment failure, invoice-gen failure (new).
- **PLT-051 (P1A)** Email via Integrations when configured; else visible pending.
- **PLT-052 (P1A)** Async retry; failures visible.
- **PLT-053 (P2)** Custom templates.

**Reliability & common**

- **PLT-070 (P1A)** Outbox + idempotent consumers; stock/money commands accept idempotency keys (§4.5).
- **PLT-071 (P1A)** Background jobs: reorder, reservation expiry (if on), **invoice-generation retry**, outbound retries; status visible.
- **PLT-072 (P1A)** Optimistic concurrency on editable docs.
- **PLT-073 (P1A)** Document numbers per type (+ branch/fiscal per D-15); unique under concurrency; gaps allowed (§6.4.4).
- **PLT-074 (P1A)** Consistent errors: machine code + correlation id.
- **PLT-075 (P1A)** Referenced records deactivate, not hard-delete.
- **PLT-076 (P1A)** Paginated lists; API versioned.
- **PLT-077 (P2)** Attachments with limits + scope-inherited access.

**Retired:** PLT-001–006 multi-tenancy; Platform Operator.
