# Review Workflow (Code and Security)

Review is analysis. Review for correctness and risk, not personal preference.

## Boundaries
- Do not modify reviewed code, stage, commit, or push unless asked. Preserve existing user changes.
- Do not approve because tests pass. Do not claim a complete review if only part was inspected; state what could not be verified.

## Steps
1. **Scope.** Identify the intent, changed files, and what is out of scope. Read the diff and the surrounding code.
2. **Correctness.** Requirements met; logic, edge cases, null/empty/boundary, off-by-one, async and concurrency, error paths.
3. **Architecture.** Code in the right layer; dependency direction respected; no entities leaked through the API; no business logic in controllers.
4. **Security.** Input validation, authentication and authorization (including resource ownership), injection, secrets, sensitive data in logs/responses, unsafe file handling, dependency risk, insecure configuration. For a dedicated security review, work through every item in the security checklist below.
5. **Data and reliability.** Transactions, migrations, concurrency, idempotency, N+1 queries, unbounded queries, timeouts, cancellation, resource disposal.
6. **Maintainability.** Naming, responsibility, duplication, unnecessary complexity or abstraction, consistency with conventions.
7. **Compatibility.** Public contract, behavior, and config changes; migration of existing data and consumers.
8. **Tests.** Cover behavior and failure paths, would catch realistic defects, not brittle, no weakened or removed tests.
9. **Validate each finding** against the code before reporting. Drop speculation.

## Severity (do not inflate)
- **Critical**: severe vulnerability, data loss, or major failure; needs immediate action.
- **High**: significant defect or security issue affecting important functionality.
- **Medium**: meaningful defect or regression in specific scenarios.
- **Low**: minor issue, limited impact.
- **Info**: non-blocking suggestion.

## Finding format
Severity · Location (file:line) · Problem · Evidence · Impact · Recommendation. Order by severity. Be specific and concise. If nothing is found, say "no findings within the reviewed scope" and list the scope. Use `templates/review-report.md` for a full report.

## Security review checklist (dedicated security reviews)
- [ ] Trust boundaries, sensitive data, and privileged operations identified.
- [ ] All input validated server-side; parameterized queries; output encoded.
- [ ] Authentication correct: password hashing, token validation (signature, issuer, audience, expiry), refresh-token rotation, rate limits on login and sensitive endpoints.
- [ ] Authorization on every endpoint and every resource; no IDOR; roles/policies not client-supplied; default deny.
- [ ] Secrets absent from code, config, logs, images; keys rotatable.
- [ ] Cryptography uses established libraries; HTTPS enforced; CORS restricted.
- [ ] Error responses leak nothing; security events logged without sensitive values; log injection prevented.
- [ ] Business logic abuse considered: replay, race conditions, mass assignment, enumeration, quota and file-upload abuse.
- [ ] Dependencies pinned and scanned; debug features and default credentials disabled in production.
- [ ] Evidence recorded for each item; limitations and outstanding risks listed.
