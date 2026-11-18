<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §6.7 · nothing changed · navigation: docs/prd/INDEX.md -->

### 6.7 Security and privacy

#### 6.7.1 Authentication

- **SEC-01 (P1A)** HTTPS/TLS everywhere.
- **SEC-02 (P1A)** Password hashing strong KDF (parameters ADR).
- **SEC-03 (P1A)** Refresh rotation + reuse detection + revocation (PLT-011).
- **SEC-04 (P1A)** Rate limiting on auth and sensitive endpoints.
- **SEC-05 (P1A)** Lockout after repeated failures.

#### 6.7.2 Authorization

- **SEC-10 (P1A)** Authentication ≠ authorization; every request re-authorizes.
- **SEC-11 (P1A)** Object-level, function-level, property-level, branch-level (BRN, PLT-020–024).
- **SEC-12 (P1A)** IDOR tests across modules (foreign branch docs, nested lines).
- **SEC-13 (P1A)** Documented **404/403 policy** = BRN-011…014; automated tests for 401/403/404 matrix.

#### 6.7.3 Secrets and data protection

- **SEC-20 (P1A)** Secrets from environment/secret store; never in repo.
- **SEC-21 (P1A)** Integration credentials encrypted at rest; never logged/returned in full.
- **SEC-22 (P1A)** Sensitive data redaction in logs (tokens, passwords, full PANs, excess PII).
- **SEC-23 (P1A)** Errors do not leak stack traces or secrets.
- **SEC-24 (P1A)** Audit and log redaction per PLT-044.
- **SEC-25 (P1B)** Invoice-generation retry endpoints: permission + idempotency (FIN-038).

#### 6.7.4 Audit integrity and retention

- **SEC-30 (P1A)** Audit write path separate from business edits; no update/delete API for audit rows.
- **SEC-31 (P1A)** Retention configurable; duration open (Q-8); export P2.

#### 6.7.5 Files

- **SEC-40 (P1A)** P1 file handling shall support authenticated company-logo upload and non-public retrieval with type and size limits.

#### 6.7.6 Webhooks

- **SEC-50 (P2)** Inbound webhook signature + timestamp/replay window (INT-005). (No P1 inbound webhook source exists; activates with the first P2 webhook source per ADR-011.)
- **SEC-51 (P2)** Outbound webhook signing secrets.

#### 6.7.7 Privacy

- **SEC-60 (P1A)** Collect only needed personal data; no special-category data required.
- **SEC-61 (P2)** Export/delete per retention policy (NFR-005).

---
