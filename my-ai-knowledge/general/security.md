# Security Standards

Technology-independent. Apply controls according to data sensitivity, exposure, and threat model. For ASP.NET Core specifics see `expertise/API_SECURITY.md`.

## Principles
- Consider security during design, not only at the end. Identify sensitive data, trust boundaries, privileged operations, and attack surface.
- Secure defaults. Fail closed when a security decision cannot be verified. Never weaken an existing control to simplify implementation.
- Escalate security uncertainty instead of inventing a solution. Consult OWASP Cheat Sheets and ASVS for control-level detail.

## Input and output
- All external input is untrusted. Validate type, format, range, and size on the server at the trust boundary. Reject invalid input rather than silently transforming it.
- Use parameterized queries and safe APIs. Never build executable statements from untrusted data. Encode output for its context.

## Authentication and authorization
- Use established mechanisms; never custom protocols or cryptography.
- Enforce authorization on every protected operation and on the specific resource (ownership, tenant). Never trust client-supplied identity, roles, or ownership.
- Least privilege. Deny by default.

## Secrets and sensitive data
- No secrets in source, config committed to Git, images, or logs. Use a secret store appropriate to the environment.
- Collect and retain only what is needed. Protect data in transit, at rest, in backups and temporary storage.
- Never return stack traces, internal paths, or database details to untrusted users.

## Cryptography
- Established libraries and current algorithms only. Manage keys securely: storage, access, rotation, revocation.

## Logging
- Log security events (auth failures, authorization violations, admin actions). Keep logs free of secrets and protected from tampering. Logging failure must not disable security controls.

## Dependencies and configuration
- Prefer maintained dependencies; pin versions with lockfiles; watch advisories; investigate unexpected changes. Restrict CI/CD credential permissions.
- Disable debug features in production. Keep environments separated. Verify security configuration instead of assuming it.

## Integrity and abuse
- Consider replay, tampering, race conditions, and business-logic abuse. Apply size, rate, and resource limits. Handle files safely.
- Treat unexpected errors, timeouts, and partial failures as security-relevant: fail closed.

## OWASP Top 10:2025 map
Access control → authz section. Misconfiguration → dependencies/configuration. Supply chain → dependencies. Cryptographic failures → cryptography. Injection → input. Insecure design → principles/integrity. Authentication failures → authn/authz. Integrity failures → dependencies/integrity. Logging failures → logging. Exceptional conditions → fail closed. Check whether the OWASP list has been updated before relying on this map.

## Verification
Test authentication, authorization (including other users' resources), validation, and failure paths. Run static analysis and dependency scans where available. Do not suppress warnings without justification.
