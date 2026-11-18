# API Security Guidelines (ASP.NET Core)

## Passwords and cryptography
- Hash passwords with a slow, dedicated algorithm (Argon2id, bcrypt, PBKDF2). Prefer ASP.NET Core Identity's `PasswordHasher`. Salts are unique per password and managed by the library. Verify with the library's verify function.
- Never store plaintext or fast hashes (SHA-256) for passwords. Encoding (Base64) is not encryption. No custom cryptography. Do not reuse one key for unrelated purposes.
- Encrypt only when data must be decrypted later; use authenticated encryption and protect keys separately.

## Transport
- HTTPS everywhere in production. Configure forwarded headers correctly behind a reverse proxy before relying on client IP.

## JWT and tokens
- Validate signature, issuer, audience, expiry, and required claims. Allow only configured algorithms and issuers; prevent algorithm confusion. With asymmetric signing, publish and rotate verification keys through a controlled mechanism (JWKS).
- Strong random signing keys, kept on the server only. Rotation process for keys and compromised credentials.
- Short-lived access tokens with only necessary claims. JWT payloads are readable, not encrypted. No large or fast-changing permission lists inside tokens.
- Re-evaluate authorization against current server state when permissions can change before expiry. Define audience, issuer, lifetime, clock skew, revocation, and logout behavior explicitly.
- Invalid token → 401. Authenticated without permission → 403.

## Refresh tokens
- Cryptographically random, stored hashed server-side, tied to a session or token family.
- Rotate on every use and invalidate the previous token. Reuse of a rotated token = possible compromise → revoke the family. Logout and revocation invalidate it. Disabled or compromised accounts cannot get new tokens.

## Authorization
- Authenticate before protected endpoints. Authorize every protected operation, not only at the controller level.
- Ownership: verify the user may access the specific resource, using the identity from validated claims. Knowing an ID is not permission. Never trust client-sent roles, user IDs, or permissions; do not rely on hidden UI.
- Roles for broad categories; policies or explicit ownership checks for specific permissions. Least privilege. Deny by default. Correct middleware order (`UseAuthentication` before `UseAuthorization`).

## CORS
- Explicit allowed origins, methods, headers. No wildcard with credentials. CORS is not authentication or authorization.
- With cookies or credentials cross-origin, use explicit origins and assess CSRF. Browser cookie sessions: `Secure`, `HttpOnly`, suitable `SameSite`, and CSRF protection for state-changing requests.

## Abuse controls
- Rate limit login, refresh, password reset, and other sensitive endpoints on several dimensions (IP and account); IP alone is insufficient. Exceeded → 429. Multiple instances need a shared limiter; in-memory limiters are per instance.
- Generic login failure messages that do not reveal whether an account exists.
- Limit request size, uploads, timeouts, and concurrency. Use idempotency keys, nonces, or deduplication for replayable operations.
- Password reset, email verification, MFA enrollment, and recovery flows: short-lived, single-use, revocable tokens and generic responses. Consider MFA or step-up for admin and high-impact actions.
- Treat redirects, callback URLs, webhooks, and outbound requests as security-sensitive inputs (open redirect, SSRF).
- Prefer standards-based OAuth 2.0 and OpenID Connect over custom authentication protocols.

## Secrets
- Never hardcode secrets in source, committed config, or deployment scripts. Development: user-secrets or environment variables. Production: Azure Key Vault or equivalent, accessed via managed identity.

## Logging and audit
- Log failed logins, authorization failures, token refreshes, and admin actions, with account identifier and event details but never passwords, tokens, or keys. Sanitize user-controlled values against log injection.
- Audit admin operations: actor, action, target, timestamp, outcome. Protect logs from unauthorized access and tampering.
- Never expose exception details or internal security information in responses.
