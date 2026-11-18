# A0 Progress — SupplyForge

Slice: **A0 Foundations** (PRD §11.2). Updated: 2026-11-18.
Authority: PRD v0.8 + accepted ADR-001…006, 008, 009. This file tracks increment
status only; it is not a requirements source.

## Increment status

| # | Increment | Requirement IDs | Status | Evidence |
|---|---|---|---|---|
| M0-0…M0-5 | Solution, Docker, DbContexts + MOD-1a split, M0 migrations applied, Platform services, Host skeleton, smoke | NFR-050, OUT-01, IDEM-01, PLT-010/011/012/040/050/073, INT-050, NFR-044 | **Done** | Build 0/0; 27 tests green at M0 close |
| A0-1 | JWT access tokens (ADR-004): standard JwtBearer handler, HS256 only, 15-min TTL, iss/aud/lifetime/signing-key validation, explicit 2-min clock skew; claims sub/jti/email; no permission catalogue in token | PLT-011 (access half), SEC-13, BRN-014 (401), NFR-045 | **Done** | Full solution 45/45 (9 arch + 14 unit + 22 integration), two consecutive runs; `dotnet format --verify-no-changes` clean; HTTP matrix boots the real Host and asserts 401 for unauthenticated/malformed/tampered/expired/wrong-issuer/wrong-audience/alg-none and 200 for a valid JWT; X-Api-Key flow verified over HTTP (INT-050) |
| A0-2 | Rate limiting on auth endpoints (SEC-04, PLT-012 second half; `Microsoft.AspNetCore.RateLimiting` is in the shared framework — no package needed) | SEC-04, PLT-012 | **Done** | Full solution 53/53 (9 arch + 14 unit + 30 integration), two consecutive runs; `dotnet format --verify-no-changes` clean; HTTP tests boot the real Host with shrunk limits (IP 6 / account 3 / 6s window) and assert 429 + Retry-After over the account limit and over the IP limit, per-dimension bucket isolation, refresh 429, multi-hop X-Forwarded-For spoof resistance (ForwardLimit=1), socket-peer vs forwarded-loopback sharing one bucket after IPv4-mapped normalization, window reset after expiry, 200 under limits, and `EnableForwardedHeaders=false` default config |
| A0-2b | Rate-limiter defect fix: bound partition cardinality (evict idle children), bound login-body buffering memory, cap account-key user-name length | SEC-04 | **Done** | Full solution 65/65 (9 arch + 26 unit + 30 integration), two consecutive runs; `dotnet format --verify-no-changes` clean; unit tests cover active-hold vs sweep (no dispose while in use), recent-use retention, idle eviction + bucket recreation, auto-sweep timer, concurrent acquire+sweep with no ObjectDisposedException, Content-Length-over-cap skip (zero read), actual body over cap without Content-Length, 101-char user name → no account key, malformed JSON, and cap invariants; all prior A0-2 HTTP tests unchanged and green |
| A0-3 | Permission catalogue, role bundles, endpoint permission metadata deny-default (PLT-020/022, PLT-ROLE-1) — **Done (9/9 steps):** fail-closed Fallback/Default + endpoint coverage · `perm:` policy provider + `IPermissionResolver` · runtime catalogue/role-bundle seed · secure create-user bootstrap (handler + `sp_getapplock` revalidation) · case/collation pins · behavioral always-true-policy scan · SEC-13 HTTP 401/403 matrix complete · verification sweep · close-out | PLT-010, PLT-020, PLT-022, PLT-ROLE-1, SEC-10, SEC-13 | **Done** | Full solution **114/114** (14 arch + 43 unit + 57 integration), two consecutive green runs in the Step 8 sweep; `dotnet format --verify-no-changes` clean; no always-true policy obtainable (behavioral scan, synthetic positive control); every protected endpoint answers anonymous → 401 and authenticated-without-permission → 403 (two theories over all 5 routes); 4 permission codes + 11 system roles seeded idempotently at Host startup; POST /users gated by the bootstrap-window handler with service-layer revalidation under an application lock; details + defects found in-step in the Step 1–9 notes below |
| A0-4 | Branch isolation + IDOR/404-403 matrix tests at HTTP level; password reset (PLT-013); audit redaction (PLT-044); OpenAPI security schemes (INT-052, ADR-006 dual auth in OpenAPI) | BRN-001…014, SEC-12/13, PLT-013, PLT-044 | **Done (S1–S3 + S5–S6; S4 PLT-013 deferred by owner)** | S1 BRN-011/013/014 HTTP status discrimination on `DELETE /api/v1/api-keys/{id}` (401 anonymous / 403 no-permission-with-204-control / 404 unknown-id) · S2 PLT-044 `SensitiveDataRedactor` at the `IAuditWriter` choke point (16 unit + 1 integration) · S3 INT-052 OpenAPI `Bearer` + `X-Api-Key` schemes with per-endpoint placement from endpoint metadata (3 tests, 7 cases) · S5 sweep green · S6 close-out. **Full solution 141/141** (14 arch + 59 unit + 68 integration), two consecutive green runs, format clean, build 0 errors 0 warnings. Deferred by decisions: PLT-013 needs migration approval; BRN-012/SEC-12 foreign-branch leg → first branch-owned resource in A1. Details in S1–S6 notes below |
| A0-5 | CI workflow (build, test, format, container build), README/runbook (NFR-061/062) | NFR-061, NFR-062, NFR-040/042 | **Done** | `.github/workflows/ci.yml`: build → format → EF migrations on the ephemeral service container → full test suite (arch incl.) → `docker build`, every step executed locally (migrations proven on a throwaway fresh DB; GH Actions themselves need the first PR); `Dockerfile` + `.dockerignore` build and smoke-run green (`/health` 200); `README.md` covers architecture overview + runbook (incl. A0-2's deferred rate-limit ops note) + generated OpenAPI. Sweep: build 0 errors 0 warnings, **141/141 ×2**, format 0, YAML OK, null-byte clean, HANDOFF identical. Carried: NFR-060 compose `app` service not in this row — not implemented |
| A0-6 | ADR-003 ambient-transaction spike: Host-owned multi-module `IDbSession` (one shared `DbConnection` + `DbTransaction` joining PlatformCore + Integrations contexts), first production adopters, ADR-003 "How tests prove it" table | MOD-TXN-01…05, OUT-01, IDEM-05, PLT-050, NFR-043(5), NFR-050 | **In progress (S1–S3 core done; S3 HTTP round-trip + S4–S6 remaining)** | Spike cases A–F resolved on a throwaway real SQL Server (plan below). S1 `AmbientDbSession`/`AmbientDbSessionFactory` in Host DI (M0 `DbSession` deleted); S2 `NotificationRaiseService` session-then-email (MOD-TXN-02); S3 six tests green 6/6 incl. IDEM-05 root-cause fix of a read-then-insert claim race. Pre-close sweep 2026-11-18: build 0/0, **147/147** (14 arch + 59 unit + 74 integration), format clean. ADR-003 status: Accepted, recommendation defaults approved — no ask-first needed for the spike itself |

## A0-1 notes (2026-11-17)

- Approved package addition: `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.15 (Host only).
- Issuance lives in `Host/Auth/JwtAccessTokenIssuer` behind `IAccessTokenIssuer`
  (PlatformCore.Application) so Infrastructure gains no token-format dependency.
  Validation is exclusively the standard JwtBearer handler — no custom parsing.
- Refresh-token rotation/reuse/family revocation (PLT-011/SEC-03) unchanged.
- Secrets via configuration: `Auth:Issuer`, `Auth:Audience`, `Auth:AccessTokenSecret`;
  override with env vars (`Auth__AccessTokenSecret`). Startup fails if the secret
  is under 32 bytes. appsettings.json holds a dev placeholder only.
- Mid-session flake: one integration failure from a cross-test audit sweep
  (`apikey.` prefix) racing another class's cleanup. Root cause fixed by scoping
  the sweep + bulk deletes; two consecutive full runs green afterwards.
- ADR-006 "single pipeline with two authenticators": X-Api-Key is still checked
  at endpoint level, not as a second registered scheme — tracked for A0-4.

## A0-2 notes (2026-11-17)

- No new packages: `Microsoft.AspNetCore.RateLimiting` + `System.Threading.RateLimiting`
  ship in the ASP.NET Core shared framework.
- Policy `auth-sensitive` (Host/RateLimiting/AuthRateLimiterPolicy) acquires two
  fixed windows per request — one per remote IP, one per account — via a small
  composite `RateLimiter`; the framework's partitioned cache holds one wrapper per
  (IP, account) key while the shared children live in policy dictionaries.
  In-memory per instance (MOD-8: no Redis). Login keys on the buffered request
  body (`LoginBodyBufferingMiddleware`); refresh is IP-keyed because refresh
  tokens are opaque and single-use (ADR-004 rotation).
- `RateLimiterOptions.RejectionStatusCode` defaults to 503 in the framework —
  set to 429 explicitly; the policy's `OnRejected` adds `Retry-After` from the
  denied lease's metadata (the middleware does not).
- Config section `RateLimit` (window, both limits, forwarded-headers opt-in,
  `ForwardLimit`, `TrustedProxies`), env-overridable. `EnableForwardedHeaders`
  defaults to false (no proxy in the A0 topology); when enabled only configured
  proxies are trusted, registered in both IPv4 and IPv4-mapped-IPv6 forms
  (`IPAddress.Equals` does not equate them), with `ForwardLimit` (default 1)
  bounding X-Forwarded-For hops.
- Test topology: the spawned Host listens on the dual-stack wildcard so IPv4
  clients arrive as `::ffff:127.0.0.1` — this exercises IP normalization and
  keeps the socket bucket ("127.0.0.1") distinct from the spoof test's
  forwarded keys. Environment overrides: `RateLimit__*`.
- appsettings.json now carries `//` comments; `HttpAuthMatrixTests` parses it
  with `CommentHandling.Skip`.

## A0-2b notes (2026-11-17)

- Defect fixed: `_ipLimiters` / `_accountLimiters` previously grew without
  bound (one `FixedWindowRateLimiter` + auto-replenish timer per distinct
  account key / IP, never removed until process exit). Account keys are
  attacker-controlled login user names — an unauthenticated memory/timer leak.
- Each child now lives in a `LimiterEntry` with `ActiveCount` + last-use
  timestamp. The dual wrapper resolves children **on every acquire** (never
  pins them at construction), holds `ActiveCount > 0` for the duration of the
  acquire, and a sweep (timer period = window) disposes entries that are
  idle > 2×window with `ActiveCount == 0`. Conditional remove-by-instance so a
  concurrent `GetOrAdd` replacement is never dropped. Stale `GetOrAdd` results
  that race disposal are detected via `Disposed` and retried.
- Idle check uses double division (`elapsed / Stopwatch.Frequency`): integer
  truncation reported 0s idle when `Frequency` is 1e9 (Linux) and would never
  trip a sub-second timeout (also fixed `IdleDuration` the same way).
- Login body buffering hard-capped at 8 KiB (`MaxBufferedBodyBytes`): a
  `Content-Length` over the cap skips `EnableBuffering` entirely (zero read);
  otherwise at most cap+1 bytes are read into a fixed buffer, then the stream
  is rewound for model binding. Over-cap → no account key → IP bucket. This
  bounds **this middleware’s** memory only — it does not replace Kestrel’s
  request-body limits.
- User-name keys capped at 100 chars (`MaxBufferedUserNameLength`, aligns with
  `User.UserName`); longer names fall back to the IP bucket.
- Test hooks are `internal` (`InternalsVisibleTo` →
  `SupplyForge.PlatformCore.UnitTests`): `TrackedLimiterCount`, `SweepNow`,
  `HoldEntriesForTest`, `ForceIdleForTest`, and a ctor flag to suppress the
  background timer for deterministic sweeps.
- No config changes; in-memory per instance (MOD-8) unchanged. Multi-instance
  behavior unchanged (limits multiply per instance; DB lockout remains the
  cross-instance brake — runbook note deferred to A0-5).

## A0-3 Step 1 notes (2026-11-17)

- **Fail-closed defaults:** `FallbackPolicy` (no endpoint metadata) and
  `DefaultPolicy` (bare `RequireAuthorization()` / bare `IAuthorizeData`) both
  use `FailClosedAuthorizationRequirement`, which never succeeds — anonymous
  **and** authenticated callers are denied. `RequireAuthenticatedUser()` alone
  is **not** used as a fallback (it would still allow any signed-in user).
- **Explicit AllowAnonymous** only on intentionally public endpoints: login,
  refresh, health. `GET /api-keys/me` also carries AllowAnonymous because it
  authenticates via X-Api-Key inside the handler (ADR-006/009); the fail-closed
  fallback would otherwise block API-key-only clients before the handler runs.
- **POST /users** declares the named `create-user` policy (not bare
  RequireAuthorization, not AllowAnonymous). Temporary Step-1 policy is open so
  existing bootstrap flows keep working; Step 4 replaces it with the real
  empty-Users bootstrap handler (`sp_getapplock`, service-layer revalidation).
- **Notifications + API-key issue/revoke** moved from bare
  `RequireAuthorization()` to named `perm:*` policies. Step-1 evaluation is
  `RequireAuthenticatedUser` only (same effective gate as before); Step 2
  replaces it with `IPermissionResolver` + catalogue checks.
- **Coverage test** (arch): every Host endpoint must declare a non-bare
  `RequireAuthorization(...)`, `create-user`, or `AllowAnonymous`. Synthetic
  cases prove bare and metadata-less registrations are rejected. Primary
  control remains the fail-closed policies; coverage is the additional
  safeguard (PLT-022).
- Temporary policy names live in `Host/Authorization/SupplyForgeAuthorization.cs`
  so unit tests evaluate the same `AuthorizationOptions` the Host registers.
- No migration, no commit, no push in this step. Next: Step 2 (`perm:`
  policy provider + `IPermissionResolver`).

## A0-3 Step 2 notes (2026-11-17)

- **Policy construction:** `PermissionPolicyProvider` (Host/Authorization)
  subclasses `DefaultAuthorizationPolicyProvider` and is registered **before**
  `AddAuthorization` so the framework's `TryAdd` keeps it. Any `perm:<code>`
  name → `RequireAuthenticatedUser()` + `PermissionRequirement(code)`;
  non-perm names (create-user, Fallback/Default) delegate to the base.
- **No `AddPolicy(perm:*)` in options anymore:** a pre-registered policy is
  returned instead of the provider's and could carry weaker requirements —
  fail open for any code someone forgets to register. The three Step-1
  registrations were removed so every perm:* name uses one evaluation path.
- **`IPermissionResolver`** sits in PlatformCore.Application/Security
  (interface only — no EF/HTTP types); `PermissionResolver` (Infrastructure
  `Application/Auth`) resolves `UserRole → RolePermission → Permission.Code`
  with one `AnyAsync` — union across all roles (PLT-020), re-read from
  current data on every request (SEC-10; ADR-004: never in the token). No
  cache in Step 2: always-DB is fresher than ADR-004's 5-minute freshness
  recommendation and is its documented "config 0" case; revisit only with
  NFR-020 measurements.
- **Handler:** `PermissionAuthorizationHandler` is scoped (transient
  `IAuthorizationHandlerProvider` resolves handlers per scope) and succeeds
  only when authenticated + resolvable subject (`ClaimTypes.NameIdentifier`,
  raw `sub` fallback — `MapInboundClaims` defaults true) + resolver true.
  Anonymous → 401 challenge; authenticated without grant → 403 (SEC-13,
  ADR-004 §6); missing/unparsable subject → deny (fail closed).
- **Unknown codes are naturally fail-closed:** the catalogue is the
  `platform.Permission` table reached through the FK chain, so a code with no
  row can never resolve true. Codes are case-sensitive in .NET and compared
  under the database's default (case-insensitive) collation; catalogue codes
  are lowercase constants.
- **DI seam:** `AddSupplyForge` registers the Infrastructure resolver;
  `AddSupplyForgeAuthorization` registers only provider + handlers, so unit
  tests inject `FakePermissionResolver` (deny-all by default) and integration
  tests grant real rows through `TestPermissionGrant`.
- **Tests:** new unit file `PermissionAuthorizationTests` (6: provider builds
  requirement + auth gate, non-perm name falls back, anonymous denied even
  when the resolver grants, exact-code grant succeeds — proving the prefix is
  stripped — missing permission denied, missing subject denied). Superseded
  Step-1 unit test `SEC_10_PermPolicy_RequiresAuthentication_NotFailClosed`
  removed (its "any authenticated user" behavior was the temporary gate this
  step replaces). Integration: `FailClosedAuthHttpTests.WithGrant` now does a
  real role-bundle grant → 200 plus new `SEC_13_..._Returns403`;
  `HttpAuthMatrixTests` minted-JWT test now expects 403 (token accepted, no
  grant) and the `INT_050` flow grants `platform.apikey.manage` before
  issuing; `SqlServerIntegrationTests.PLT_020_...Union...` proves multi-role
  union + deny default on real SQL. `TestPermissionGrant` upserts shared
  catalogue rows race-safe (unique `Permission.Code`: first writer wins,
  losers re-query) and deletes only per-test role rows.
- **First full run after the change:** 7 integration failures, all
  cold-start environment flakes — socket timeouts and
  `SEC_04_RefreshOverLimit_Returns429` seeing 401 (slow first-run requests
  outlast the 6s fixed window, so the bucket replenished mid-test). That path
  is untouched: refresh is AllowAnonymous and never reaches the resolver.
  Two consecutive full runs green afterwards (89/89).
- No migration, no commit, no push in this step. Open questions for the
  owner: A0-3 steps 3 and 5–9 are not defined anywhere on disk; and no code
  or seed yet assigns roles to users (tests grant rows directly) — who seeds
  the catalogue/role bundles and how demo users get roles (create-user
  bootstrap in Step 4?).

## A0-3 Step 3 notes (2026-11-17)

- **Steps 3–9 now defined on disk** (answering the Step 2 open question):
  **3** permission catalogue + system role-bundle seed (this step) ·
  **4** secure create-user bootstrap (replace the temporary always-true
  `create-user` policy with the empty-Users bootstrap handler —
  `sp_getapplock` + service-layer revalidation) · **5** permission-code
  case/collation behavior tests · **6** authorization coverage hardening
  (reject always-true named policies) · **7** complete SEC-13 HTTP matrix ·
  **8** verification sweep (two green runs + format) · **9** A0-3 close-out
  notes. Role/branch assignment stays out of A0-3: it is MD-004 → slice A1.
- **Mechanism:** `PlatformCatalogueSeed` (Infrastructure/Data) invoked from
  `Program.cs` in a DI scope before `app.Run()`. Deliberately runtime
  get-or-insert, **not** EF `HasData` — `HasData` would need a migration and
  migrations require owner approval; this keeps A0-3 migration-free.
- **Content (PLT-020 / PLT-ROLE-1):** 4 permission codes — the three Host
  `perm:` policy codes plus `platform.user.manage` (needed by Step 4) — and
  the 11 system roles named in PRD 02b §2.6.1, `IsSystem = true`. Exactly one
  bundle mapping, the only one the PRD states: `company_administrator →
  platform.user.manage` (02b §2.6.1 "Company Administrator | Users, roles,
  settings…"). No other mappings — module permission matrices (PRD §6.4.1)
  define them when each module lands. The PRD defines no `platform.*` codes;
  code names are implementation mechanism, now centralized in
  `PermissionCodes` (PlatformCore.Application/Security), from which the Host
  policy names are built — a policy can no longer drift from its catalogue row.
- **Idempotency/race safety:** get-or-insert against the unique `Code` indexes
  (first writer wins; loser of an insert race re-queries the winner's committed
  row, and only a failure where the row is still missing rethrows — same
  pattern as `TestPermissionGrant`). Set-based `Module` backfill for the
  seed's own codes completes rows that predate the seed (the shared test DB
  already held catalogue rows created by Step 2's helper without `Module`).
- **Tests:** new `CatalogueSeedTests` (3, real SQL): every Host `perm:`
  policy code + `platform.user.manage` has a catalogue row with `Module` set;
  the 11 system roles exist exactly once, all `IsSystem`, and the admin
  bundle grants exactly `platform.user.manage`; two concurrent seeds leave
  exactly one row per code. First run caught a real defect — pre-existing
  rows without `Module` were skipped by get-or-insert — fixed in the seed
  (backfill), not by weakening the assertion.
- **Verification:** full solution 92/92 (12 arch + 39 unit + 41 integration),
  two consecutive green runs; `dotnet format --verify-no-changes` clean.
- No migration, no commit, no push in this step. Next: Step 4 (create-user
  bootstrap).

## A0-3 Step 4 notes (2026-11-17)

- **Closes the temporary always-true `create-user` policy** (PLT-022 deny-
  default, SEC-10 re-authorization per request, PLT-010 admin-created users).
  Policy now carries `CreateUserAuthorizationRequirement` +
  `CreateUserAuthorizationHandler` (Host/Authorization): **Users table empty →
  allow** (first-run bootstrap window); otherwise the caller must be
  authenticated with a resolvable subject holding `platform.user.manage`
  (resolved per request through `IPermissionResolver`, never from the token —
  ADR-004). Failure surfaces through the pipeline as 401 anonymous / 403
  authenticated (SEC-13).
- **`IUserAccountState`** (PlatformCore.Application/Security) +
  `UserAccountState` (Infrastructure): the Host handler never touches the
  DbContext (API layer rule). Subject extraction was unified in
  `PrincipalSubject` and is shared by the perm: handler, the create-user
  handler, and the endpoint — the subject the policy checks is by construction
  the subject handed to the service.
- **Service-layer revalidation** (`AuthService.CreateUserAsync` new
  `actingUserId`): one transaction owns `sp_getapplock`
  (`supplyforge.user.create`, `@LockOwner='Transaction'`, 5s timeout, negative
  return code → throw) → re-check the invariant (no users yet **or** actor
  currently holds `platform.user.manage`) → `user_exists` check → insert →
  audit → commit. The lock is released by commit/rollback; user and audit row
  are atomic. Concurrent first-run creates: the lock winner inserts, the loser
  revalidates and gets `forbidden` (endpoint maps it to 401/403 by identity).
  A0 has no ambient transaction yet, so `BeginTransactionAsync` is safe until
  A0-6 joins ADR-003's ambient session.
- **Test fixtures reworked** (POST /users is no longer an anonymous fixture):
  new `TestUsers.CreateAsync` inserts a login-capable user directly with the
  real `PasswordHasher`. Call sites: `FailClosedAuthHttpTests` ×4,
  `RateLimitHttpTests` ×1, `HttpAuthMatrixTests` ×1. Direct service callers
  (`SqlServerIntegrationTests` PLT_010/011/012) now create a manager actor
  with `CreateManagerAsync` (direct user + `TestPermissionGrant` of
  `platform.user.manage`) because revalidation applies to every caller.
- **Tests added:** unit `CreateUserAuthorizationTests` (4 — bootstrap window
  open for anonymous, `platform.user.manage` allowed and requested by exact
  code, no-permission denied, unresolvable subject fails closed even with a
  grant-all resolver); integration `SEC_13_CreateUser_Anonymous_Returns401_WhenUsersExist`,
  `SEC_13_CreateUser_AuthenticatedWithoutManagePermission_Returns403`,
  `PLT_010_CreateUser_WithoutActor_WhenUsersExist_ReturnsForbidden`,
  `PLT_010_CreateUser_ConcurrentCreates_Succeed_SerializedByAppLock`.
  Superseded: HTTP `SEC_10_Fallback_DoesNotOverride_CreateUserCustomPolicy`
  (its non-override proof now lives at unit level, where the bootstrap window
  can be faked; the HTTP counterpart became the SEC-13 401 test).
- **Honest test gap:** the empty-Users path cannot be exercised over HTTP
  against the shared persistent test database (emptying `platform.User` would
  delete data). It is covered by the handler unit tests plus the service
  revalidation test; a true end-to-end first-run test would need a throwaway
  migrated database (migration approval — owner question, not blocking).
- **Defects found and fixed in-step:** `SqlQueryRaw` cannot execute the
  `DECLARE/EXEC` lock batch (EF composes it into a subquery → non-composable
  SQL), so the lock runs as raw ADO enlisted via
  `IDbContextTransaction.GetDbTransaction()`; `AuthorizationHandlerContext`
  has no `CancellationToken` (handlers pass none, matching the perm: handler).
- **Verification:** full solution 99/99 (12 arch + 43 unit + 44
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean. No migration, no commit, no push. Next: Step 5 (permission-code
  case/collation behavior tests).

## A0-3 Step 5 notes (2026-11-17)

- **Test-only step** (engineering regression guard for PLT-020 — no new
  requirement, no production code change): new `PermissionCodeCaseTests`
  (3, real SQL) freezes how permission codes compare, because codes are
  ordinal .NET constants while resolution happens in SQL under the database
  collation.
- **Observed behavior pinned:** database collation is
  `SQL_Latin1_General_CP1_CI_AS` (asserted by name, with a failure message
  that explains the dependency); the exact lowercase code and its wrong-case
  form both resolve for a granted user; an unknown code stays fail-closed
  regardless; a case-variant duplicate of a seeded code
  (`PLATFORM.USER.MANAGE`) is rejected by the unique `Permission.Code` index,
  so the catalogue can never contain two rows resolution would treat alike;
  all rows with module metadata (the seeded catalogue) are lowercase.
- **No refactors:** codes were already centralized in `PermissionCodes`
  (Step 3); this step deliberately adds no new constants class or changes to
  resolution logic (library rule 15).
- **Environment note:** the session host restarted mid-step and corrupted the
  freshly written test file (null bytes); detected by the build (CS1056) and
  rewritten — no other file affected (null-byte scan clean).
- **Defect found in-step:** `SqlQueryRaw<string>` requires the result column
  aliased `AS [Value]` when EF composes over it (otherwise "Invalid column
  name 'Value'").
- **Verification:** full solution 102/102 (12 arch + 43 unit + 47
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean. No migration, no commit, no push. Next: Step 6 (authorization
  coverage hardening — reject always-true named policies).

## A0-3 Step 6 notes (2026-11-18)

- **Coverage hardening** (PLT-022 deny-default, SEC-10): new
  `AlwaysTruePolicyTests` (ArchitectureTests, 2 tests) adds a *behavioral*
  layer next to the existing source scan. The source scan proves endpoints
  *declare* a named policy; this proves no obtainable policy actually
  *grants* access to both an anonymous and a normal authenticated caller —
  an always-true policy would defeat the endpoint scan, the fail-closed
  fallback, and the permission check simultaneously.
- **Detector:** builds the real `AddSupplyForgeAuthorization` configuration
  in a DI container with local stubs (`StubUserAccountState` — users exist,
  so the create-user bootstrap window is closed; `StubPermissionResolver` —
  deny-all, so the caller is a normal permission-less user). Candidates =
  FallbackPolicy + DefaultPolicy (via `IAuthorizationPolicyProvider`) +
  **every registered named policy** + provider-built `perm:*` policies for
  every code in `PermissionCodes`. Each candidate is evaluated against an
  anonymous principal and an authenticated-with-subject principal; a policy
  succeeding for **both** is a violation.
- **Why reflection:** .NET 10 `AuthorizationOptions` has no public policy
  enumeration (`GetPolicies` does not exist — compile error, checked against
  the 10.0.15 ref pack), and the endpoint source scan accepts *any* non-bare
  named policy, so a future named policy would otherwise escape every
  behavioral check. The private registry is read via reflection
  (`Dictionary<string, Task<AuthorizationPolicy>>` backing field, discovered
  empirically with a scratch console) and **throws loudly** if that internal
  shape ever changes — an unenumerable options object would make the scan
  vacuous, and a hard failure is the safe direction. Honesty is pinned by
  the positive test: it registers a synthetic always-true policy and
  requires the detector to flag it.
- **Vacuous-pass guard:** the negative test first asserts every expected
  policy name (Fallback, Default, create-user, 4 `perm:*`) was actually
  evaluated, then asserts zero violations with the violation list in the
  failure message.
- **Result:** synthetic `test:always-true` detected; the real configuration
  yields zero violations.
- **Verification:** full solution 104/104 (14 arch + 43 unit + 47
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean. No migration, no commit, no push. Next: Step 7 (complete the
  SEC-13 HTTP 401/403 matrix).

## A0-3 Step 7 notes (2026-11-18)

- **Scope:** SEC-13's automated 401/403/404 matrix — the 404/403 branch part
  (BRN-011…014) is A0-4 per the roadmap row; this step completes the
  *auth-related* 401/403 matrix over the A0-3 endpoint surface.
- **Coverage gap found:** of the 5 protected endpoints, only GET
  notifications and POST api-keys had an anonymous-401 assertion, and only
  GET notifications (+ POST users via a different class) had an
  authenticated-without-permission 403. POST notifications, POST api-keys
  (403 leg), and both legs of DELETE api-keys were untested.
- **Fix (in `HttpAuthMatrixTests`, host already booted there):** one
  `TheoryData` listing every protected route (GET+POST `/api/v1/notifications`,
  POST+DELETE `/api/v1/api-keys`, POST `/api/v1/users`) driving two theories —
  `SEC_13_ProtectedEndpoints_Anonymous_Return401` (challenge before the
  endpoint runs) and `SEC_13_ProtectedEndpoints_AuthenticatedWithoutPermission_Return403`
  (minted valid JWT, subject in no bundle — 401 here would mean token
  rejection, 403 means authorization denial). No request bodies: the
  authorization middleware short-circuits before model binding. A third test
  pins the public side: health stays 200, and `GET /api/v1/api-keys/me`
  (AllowAnonymous so X-Api-Key clients pass the fail-closed fallback) answers
  a missing header with its own handler-level 401.
- **Superseded:** `SEC_13_Unauthenticated_ProtectedEndpoints_Return401`
  covered only 2 of 5 routes; its two assertions and the health check are
  subsumed by the full theory + the new public-endpoints test. `PLT_011`'s
  valid-JWT-403 test stays (distinct requirement ID and token-validity
  evidence).
- **No rate-limit interference:** `RequireRateLimiting` attaches only to
  login/refresh; the matrix endpoints are unlimited (AuthIpLimit=30/5min
  would not matter here anyway).
- **Verification:** full solution 114/114 (14 arch + 43 unit + 57
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean. No migration, no commit, no push. Next: Step 8 (verification
  sweep).

## A0-3 Step 8 notes (2026-11-18)

- **Verification sweep** (no code changes): `dotnet build` → 0 errors;
  `dotnet test` ×2 consecutive → **114/114 each run** (14 arch + 43 unit +
  57 integration, 0 failed/skipped); `dotnet format --verify-no-changes` →
  exit 0.
- **Hygiene checks:** null-byte scan over `src`/`tests`/`docs`/HANDOFF/AGENTS
  clean (guards against the host-restart corruption seen in Step 5);
  `HANDOFF.md` and `docs/HANDOFF.md` byte-identical.
- No migration, no commit, no push. Next: Step 9 (A0-3 close-out notes).

## A0-3 close-out (2026-11-18) — 9/9 steps done

- **Delivered** (details in the Step 1–9 notes above): deny-by-default
  Fallback/Default policies + endpoint source coverage (Step 1); `perm:*`
  policies evaluated per request through `IPermissionResolver` (Step 2);
  idempotent runtime seed of 4 permission codes + 11 system role bundles,
  migration-free via `HasData`-avoidance (Step 3); create-user bootstrap —
  always-true policy replaced by the empty-Users window handler, with
  service-layer revalidation under `sp_getapplock` and atomic user+audit
  commit (Step 4); permission-code case/collation behavior pinned (Step 5);
  behavioral always-true-policy scan with a synthetic positive control and
  a vacuous-pass guard (Step 6); complete SEC-13 HTTP 401/403 matrix over
  all 5 protected endpoints (Step 7); verification sweep (Step 8).
- **Final evidence:** build 0 errors; **114/114** (14 arch + 43 unit + 57
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  exit 0; null-byte scan clean; both HANDOFF copies identical.
- **Defects found and fixed in-step (5):** seed skipped pre-existing
  catalogue rows without `Module` → set-based backfill; `SqlQueryRaw` cannot
  run the `DECLARE/EXEC` applock batch → raw ADO enlisted via
  `IDbContextTransaction.GetDbTransaction()`; `AuthorizationHandlerContext`
  has no `CancellationToken`; `SqlQueryRaw<string>` needs `AS [Value]` when
  composed; a wrong `PLT-041` citation removed (PLT-041 is audit
  immutability, not write atomicity).
- **Carried forward, not blocking A0:** (1) a true end-to-end first-run
  create-user test needs a throwaway migrated database → migration approval
  (owner question; currently covered by handler unit tests + the
  service-layer revalidation test); (2) role/branch assignment to users is
  MD-004 → slice A1; (3) D1 ADR-range fix in root `AGENTS.md` still pending
  owner approval.
- No migration, no commit, no push anywhere in A0-3. **Next: A0-4**
  (BRN-001…014 HTTP 404/403 matrix, PLT-013 password reset, PLT-044 audit
  redaction, INT-052 OpenAPI security schemes).

## A0-4 step plan (defined 2026-11-18)

- **S1** BRN-011/013/014 + SEC-12 HTTP matrix on the existing
  id-addressed resource (`DELETE /api/v1/api-keys/{id}`): 401 / 403 / 404
  discrimination on one route, unknown-id → 404 (BRN-011), known in-scope
  without function permission → 403 (BRN-013), unauthenticated → 401
  (BRN-014). **BRN-012's foreign-branch 404 leg cannot be HTTP-tested in
  A0**: the Branch entity belongs to Master Data (A1) and A0 stores only
  opaque branch ids — no branch-owned HTTP resource exists yet. Documented
  here as deferred to the first branch-owned resource in A1 (SEC-12's real
  IDOR target: foreign-branch documents).
- **S2** PLT-044 sensitive-field redaction: code-configured redaction list
  applied to audit payloads and logs + tests (no schema change).
- **S3** INT-052 OpenAPI from code: security schemes (JWT bearer +
  X-Api-Key per ADR-006/009) on the generated document + HTTP test.
- **S4** PLT-013 password reset (expiring single-use link + admin reset
  fallback per Q-5 default = manual OK): **owner decision 2026-11-18:
  deferred out of A0-4** — needs a schema change (reset-token state on
  `platform.User`) and the migration was not approved. Revisit when the
  owner approves a migration; not part of this increment.
- **S5** verification sweep (build, two consecutive green `dotnet test`,
  `dotnet format --verify-no-changes`).
- **S6** A0-4 close-out notes.

## A0-4 S1 notes (2026-11-18)

- **BRN-011/013/014 + SEC-12 HTTP status discrimination** on the only
  id-addressed route A0 has (`DELETE /api/v1/api-keys/{id}`), three new
  tests in `HttpAuthMatrixTests`:
  `BRN_014_ApiKeyRevoke_Unauthenticated_Returns401` (authentication first —
  anonymous learns nothing about existence),
  `BRN_013_ApiKeyRevoke_KnownId_WithoutPermission_Returns403_WithPermission_Returns204`
  (known in-scope resource + missing function permission → 403; the 204
  control on the same id proves the 403 is a permission denial and not an
  existence/route artifact — BRN-003 no id bypass),
  `BRN_011_ApiKeyRevoke_UnknownId_WithPermission_Returns404` (existence
  disclosed only to callers who pass the permission check).
- **BRN-012 / SEC-12 foreign-branch leg deferred to A1 by construction:**
  the Branch entity belongs to Master Data (A0 stores opaque branch ids
  only) and no branch-owned HTTP resource exists in A0 — the real IDOR
  target (foreign-branch documents, nested lines) appears with the first
  branch-owned resource in A1. Recorded in the step plan above.
- **Verification:** full solution 117/117 (14 arch + 43 unit + 60
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean. No migration, no commit, no push. Next: S2 (PLT-044 redaction).

## A0-4 S2 notes (2026-11-18)

- **PLT-044 sensitive-field redaction**, code-configured list + tests:
  new `SensitiveDataRedactor` (Shared/Auditing) with the PRD list as
  configured markers — passwords, token hashes, webhook/API-key secrets,
  payment instrument numbers (last 4 kept), national ids — matched
  case-insensitively as substrings (over-redaction is the safe direction;
  "password" also catches passwordHash, "secret" catches apiKeySecret).
  Two paths: structured recursive redaction for JSON audit payloads
  (objects/arrays, nested), pattern redaction for free-text `key=value`
  detail strings and log messages (`RedactText` for log call sites).
- **Applied at the one choke point:** `IAuditWriter` is how every module
  writes audit details (MOD-1a), so `PlatformSharedServices.WriteAsync`
  redacts `detailsJson` before persistence — callers cannot bypass it.
  The only existing log call site (unhandled-exception correlation id) is
  already free of sensitive data; `RedactText` is the configured path for
  future log lines and is tested directly.
- **Tests:** new unit `SensitiveDataRedactorTests` (9: every configured
  field redacted in JSON, payment last4 only, nested recursion, non-JSON
  key=value, unknown fields untouched, null passthrough, PRD-category list
  pin, log-text redaction) + integration
  `PLT_044_AuditLog_SensitiveDetails_RedactedBeforePersist` (real SQL: the
  stored AuditLog row contains neither password, API-key secret nor full
  card number; visible fields intact).
- **Defects found and fixed in-step:** `JsonNode.TryParse`/
  `JsonDocument.TryParse` do not exist in this API surface (used
  `JsonException` as the parse gate); regex `\b` prevented mid-word matches
  like `apiKeySecret=` (dropped `\b`, documented — marker still requires a
  trailing `[=:]` so prose cannot match); assigning an already-parented
  `JsonNode` back to its own parent throws (nested containers now mutate in
  place).
- **Verification:** full solution 134/134 (14 arch + 59 unit + 61
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean. No migration, no commit, no push. Next: S3 (INT-052 OpenAPI
  security schemes).

## A0-4 S3 notes (2026-11-18)

- **INT-052 / ADR-006 §8–9 — OpenAPI from code documents both
  authenticators** of the single pipeline (ADR-004/006 Bearer JWT +
  ADR-009 frozen `X-Api-Key` header):
  - New `SupplyForgeOpenApi` (Host/OpenApi) registers two document/
    operation transformers on `AddOpenApi`:
    `AddSecuritySchemes` puts `Bearer` (http/bearer/JWT) and `X-Api-Key`
    (apiKey in header, name `X-Api-Key`) into `components.securitySchemes`
    with ADR references; `ApplyEndpointSecurity` adds a **Bearer
    requirement only where the endpoint declares `IAuthorizeData`** and
    nothing where it declares `AllowAnonymous` — the contract mirrors the
    observed 401/403 matrix from S1/A0-3, and no global `security` block
    exists (public endpoints stay honestly unsecured).
  - `GET /api/v1/api-keys/me` (AllowAnonymous to the fail-closed policy
    but handler-challenged on the header) declares an `X-Api-Key`
    requirement via an endpoint-site `AddOpenApiOperationTransformer` —
    the one route where apiKey auth actually applies.
- **Defect found and fixed in-step (real, behavioral):** `GET
  /openapi/v1.json` returned **401** — `MapOpenApi()` had no explicit
  `AllowAnonymous`, so the fail-closed FallbackPolicy (A0-3 Step 1)
  challenged it. Fixed with explicit `.AllowAnonymous()` next to the
  Development-only mapping (same pattern/commentary as `/health`,
  PLT-022). Found by booting the host manually before writing the test.
- **Pre-existing warning fixed in-step:** `PermissionPolicyProvider`
  emitted CS8604 (nullability narrowing from its `is not null` guard into
  the non-nullable base call) whenever the Host project recompiles —
  fixed at the root with a contract-justified `policyName!` + comment;
  build is now **0 errors, 0 warnings**.
- **OpenApi.NET 2.13 API notes:** `OpenApiSecurityScheme`/`OpenApiSecurityRequirement`
  live in `Microsoft.OpenApi` (post-2.0 namespace), requirements key on
  `OpenApiSecuritySchemeReference(id, hostDocument)`, and
  `OpenApiDocument.Components`, `OpenApiComponents.SecuritySchemes`,
  `OpenApiOperation.Security` are all **nullable-annotated** (verified via
  `NullabilityInfoContext`) — handled explicitly, no `!` suppression.
- **Tests (3 new, `HttpAuthMatrixTests`; test host now boots with
  `ASPNETCORE_ENVIRONMENT=Development` so `MapOpenApi` maps):**
  `INT_052_OpenApi_DocumentsBearerAndXApiKeySecuritySchemes` (scheme
  shapes + no global security),
  `INT_052_OpenApi_ProtectedOperations_DeclareBearerRequirement`
  (all 5 protected routes → exactly `{"Bearer": []}`, concrete matrix ids
  mapped onto the `{id}` route template),
  `INT_052_OpenApi_PublicOperations_DeclareNoSecurity_AndApiKeyRouteUsesXApiKey`
  (health/login/refresh → no requirement; api-keys/me → X-Api-Key only).
- **Verification:** full solution 141/141 (14 arch + 59 unit + 68
  integration), two consecutive green runs; `dotnet format --verify-no-changes`
  clean; build 0 errors 0 warnings. No migration, no commit, no push.
  Next: S5 (verification sweep), then S6 (A0-4 close-out).

## A0-4 S5/S6 close-out (2026-11-18) — increment closed

- **S5 verification sweep** (no code changes): `dotnet build` → 0 errors,
  0 warnings; `dotnet test` ×2 consecutive → **141/141 each run** (14 arch
  + 59 unit + 68 integration, 0 failed/skipped);
  `dotnet format --verify-no-changes` → exit 0; null-byte scan over
  `src`/`tests`/`docs`/HANDOFF/AGENTS clean; `HANDOFF.md` and
  `docs/HANDOFF.md` byte-identical.
- **Delivered** (details in S1–S3 notes above):
  1. **S1** — BRN-011/013/014 + SEC-12 HTTP status discrimination on the
     only id-addressed A0 route (3 tests; 204-control proves the 403 is a
     permission denial, not an existence artifact).
  2. **S2** — PLT-044 sensitive-field redaction: configured marker list,
     applied at the one `IAuditWriter` choke point all modules share,
     JSON + `key=value` paths (9 unit + 1 integration test).
  3. **S3** — INT-052 OpenAPI from code: both ADR-006/009 auth schemes in
     `components.securitySchemes`, Bearer requirement applied only where
     endpoint authorization metadata declares it, `X-Api-Key` on its one
     real route, public endpoints unsecured in the contract (3 tests /
     7 cases).
- **Defects found and fixed in-step (4):** OpenAPI document endpoint
  returned 401 (fallback policy caught `MapOpenApi` — fixed with explicit
  `AllowAnonymous`); pre-existing CS8604 nullability warning in
  `PermissionPolicyProvider`; `Assert.Single(JsonElement)` /
  concrete-id-vs-`{id}`-template mismatches in the new test (fixed in the
  test, not by weakening assertions).
- **Deferred with decisions on record:**
  PLT-013 password reset (S4) — **owner decision 2026-11-18**: needs a
  migration, not approved; revisit when approved.
  BRN-012/SEC-12 foreign-branch 404 leg — no branch-owned HTTP resource
  in A0 (Branch = Master Data); lands with the first branch-owned
  resource in A1.
- **Carried from A0-3 (unchanged):** empty-DB first-run HTTP bootstrap
  test needs migration approval; role/branch assignment → MD-004 → A1;
  D1 ADR-range fix in root `AGENTS.md` pending owner approval.
- No migration, no commit, no push anywhere in A0-4. **Next: A0-5**
  (CI workflow + README).

## A0-5 step plan (defined 2026-11-18)

- **S1** Dockerfile for the Host (multi-stage sdk→aspnet, NFR-061
  "container build" target) + local `docker build` verification.
- **S2** `.github/workflows/ci.yml` on PR: `dotnet build` ·
  `dotnet format --verify-no-changes` (NFR-042) · SQL Server service
  container + `dotnet ef database update` for both module migration sets
  (PlatformCore + Integrations; design-time factories exist) + full
  `dotnet test` incl. architecture tests (NFR-040, NFR-061) ·
  `docker build` (NFR-061). Validate YAML locally and run every step
  locally — GH Actions themselves cannot execute in this environment.
- **S3** README (NFR-062): what SupplyForge is, architecture overview
  (modular monolith, module/table ownership, MOD rules), runbook
  (compose SQL → run host → migrate [ask-first] → test; rate-limit
  multi-instance note deferred from A0-2; OpenAPI in dev), pointers to
  PRD/ADRs. Architecture diagrams stay `SF.png` (read-only).
- **S4** verification sweep (build, two green runs, format, container
  build, YAML validation).
- **S5** A0-5 close-out notes.
- **Scope note:** NFR-060 ("Compose path: app + SQL Server") is *not* in
  the A0-5 roadmap row — the Dockerfile is created because NFR-061 needs
  a container-build target; adding an `app` service to compose is recorded
  as carried-forward, not implemented here.

## A0-5 S1/S2 notes (2026-11-18)

- **S1 Dockerfile (NFR-061 target):** multi-stage
  `mcr.microsoft.com/dotnet/sdk:10.0` → publish Host →
  `mcr.microsoft.com/dotnet/aspnet:10.0`, config only via env at run time
  (SEC-20; nothing baked in), plus `.dockerignore` (bin/obj/.git).
  Verified locally **end-to-end**: `docker build` green; smoke run with a
  published port returned `GET /health` → 200 and logs showed the startup
  catalogue seed succeeding against the compose SQL Server.
- **S2 `.github/workflows/ci.yml` (NFR-061/060-adjacent, NFR-040/041/042):**
  on PR (and push to main): checkout → setup-dotnet 10.0.x → `dotnet build`
  → `dotnet format --verify-no-changes` → install `dotnet-ef` 10.0.15 →
  `dotnet ef database update` for **both** module migration sets →
  `dotnet test` (architecture + unit + integration) → `docker build`.
  SQL Server 2022 runs as a job **service container** (same image,
  healthcheck, and documented dev-placeholder credentials as the local
  compose; tests' hardcoded `localhost,1433` matches the service port).
  Migrations in CI run only against that job's ephemeral database — never a
  shared environment.
- **CI migration step verified without touching the dev DB:** spun up a
  throwaway SQL container on port 1434 and ran the exact two
  `dotnet ef database update` commands with `--connection` pointed at it —
  both `InitialPlatformCore` and `InitialIntegrations` applied cleanly to a
  fresh, empty database (which also proves EF creates the database on first
  update). Throwaway container/volume removed afterwards; the shared dev
  database was never the target (a read-only `migrations list` against it
  confirmed connectivity earlier).
- **Defect found and fixed in-step (own):** first throwaway run published
  `-p 1443:1443` but SQL Server listens on **1433 inside** the container —
  TCP accept without a backend produced the misleading "pre-login handshake"
  failure. Republished as `1434:1433` and everything worked; recorded here
  because the same shape of error (connection established, handshake dies)
  usually means "wrong container port mapping".
- **Honest limitation:** GitHub Actions cannot execute in this environment
  (no `act`/`actionlint` installed). The workflow was validated by YAML
  parse and by running **every step it declares** locally (build, format,
  migrations-on-a-fresh-DB, full test suite, container build).
  Real runs start at the first PR once the repo has commits.

## A0-5 S3 notes (2026-11-18)

- **README.md (NFR-062):** what SupplyForge is (portfolio backend, not an
  ERP), stack, architecture overview (module/ownership table, MOD rules as
  enforced by the architecture tests, auth model), repo map, **runbook**
  (compose SQL → run host → first-run bootstrap user + login curls →
  secrets via env → migrations marked *ask first* → tests/format), the
  deferred **multi-instance rate-limit operations note from A0-2**
  (in-memory per instance; DB lockout is the cross-instance brake),
  generated OpenAPI (Development `GET /openapi/v1.json`, both schemes), the
  Docker image build/run, CI summary, and pointers to the PRD index/ADRs.
- Verified against reality, not assumed: port **5009** (launchSettings http
  profile, not the framework default 5000), request/response field names
  taken from `CreateUserRequest`/`LoginRequest`/`AuthResponse`, compose
  `.env` requirement from the actual compose file, and Platform Core's
  ownership worded as *branch/warehouse scope* (the Branch entity itself is
  Master Data — A1).

## A0-5 S4/S5 close-out (2026-11-18) — increment closed

- **S4 verification sweep:** `dotnet build` → 0 errors 0 warnings;
  `dotnet test` ×2 → **141/141 each run** (14 arch + 59 unit + 68
  integration); `dotnet format --verify-no-changes` → exit 0; container
  build green; workflow YAML parses; null-byte scan clean; both HANDOFF
  copies identical.
- **NFR coverage:** NFR-061 (build/test/format/container on PR) and
  NFR-042 (format in CI) in `ci.yml`; NFR-040 (arch tests in CI) via
  `dotnet test`; NFR-041 (unit + integration per module) unchanged, now
  CI-run; NFR-062 (README, architecture overview, runbook, generated
  OpenAPI) in `README.md`.
- **Carried forward:** NFR-060 ("one Compose path: app + SQL Server") —
  the image now exists and was smoke-tested, but the A0-5 roadmap row does
  not list NFR-060, so the `app` service was **not** added to
  `docker-compose.yml`; ask or assign before wiring it. PLT-013 still
  awaits migration approval.
- No migration against the dev database, no commit, no push.
  **Next: A0-6 (ADR-003 ambient-transaction spike) → A0 gate evidence
  review → `/slice A1`.**

## A0-6 step plan (defined 2026-11-18)

**Authority:** `ADR/ADR-003.md` (Accepted; "recommendation defaults
approved" — no ask-first needed for the spike design). Its Follow-up line —
*"Ambient session must exist before first multi-module write (A0
infrastructure)"* — is this increment. PRD IDs grepped from
`04c-idempotency-transactions-outbox.md` (MOD-TXN-01…05, OUT-01,
IDEM-01…06), `08-non-functional.md` (NFR-013/043/050),
`11-delivery-plan.md` (A0 row).

**Files read before coding:** PRD `INDEX.md`, `00-how-to-use.md` (earlier in
session), the three requirement greps above; `ADR/ADR-003.md` in full; library
`workflows/implementation.md`, `expertise/EF_CORE.md`,
`expertise/DOTNET_TESTING.md`, `general/testing.md`.

**Spike evidence (throwaway SQL container `spike-tx-sql`, scratch project
`/tmp/opencode/spiketx`, NOT the dev DB):** the mechanism was proven against
EF 10 + Microsoft.Data.SqlClient before writing production code:

- **Case A (negative):** two contexts on *separate* connections +
  `UseTransactionAsync(tx)` on the second → rejected
  ("transaction is not associated with the current connection") — shared
  connection is mandatory, TransactionScope/MSDTC remain forbidden
  (MOD-TXN-01).
- **Case B (positive):** `Database.SetDbConnection(shared)` +
  `UseTransaction` on **both** contexts → one physical connection
  (`ReferenceEquals` true), cross-context commit works.
- **Case C:** forced failure after outbox insert → rollback leaves **0**
  domain rows + **0** outbox rows (OUT-01 atomicity holds).
- **Cases D–F (restore semantics — decided the design):**
  - `SetDbConnection` **disposes EF's own original connection** (its
    ConnectionString is scrubbed) — the original object can never be
    restored;
  - `SetDbConnection(null)` does **not** fall back to the connection string
    (EF lazily creates a *stringless* connection → next open throws) —
    unusable as a restore;
  - restoring a **fresh connection built from the captured connection
    string** works on every path — commit, rollback, and
    dispose-without-commit — after which each context is fully independent
    again (proved even after the shared connection itself is disposed);
  - the `IDbContextTransaction` returned by `UseTransactionAsync` **owns**
    the raw transaction (wrapper dispose disposes it; double-dispose is
    idempotent);
  - restore connections are caller-owned (EF does not dispose them at
    context dispose) — closed, pooled, zero server resource held.

**Steps:**

- **S1 — Host-owned ambient session.** New `AmbientDbSession` /
  `AmbientDbSessionFactory` in `src/Host/SupplyForge.Host/Data/` implementing
  the Shared `IDbSession` / `IDbSessionFactory`: `BeginAsync` captures both
  contexts' connection strings → opens one shared `SqlConnection` +
  `DbTransaction` → `SetDbConnection(shared)` + `UseTransaction` on
  PlatformCore **and** Integrations contexts (joiners enlist); `CommitAsync`,
  `RollbackAsync`, and `DisposeAsync` (dispose-without-commit ⇒ rollback)
  funnel into one idempotent cleanup in the proven order: dispose wrappers →
  restore fresh connections from the captured strings → dispose shared.
  Swap the DI registration in `ServiceCollectionExtensions` (Host owns
  composition; PlatformCore must not reference Integrations — MOD-1/arch
  tests). Remove the M0 single-context `DbSession`/`DbSessionFactory`
  (superseded; zero remaining call sites outside that registration).
- **S2 — first production adopter: notification raise (PLT-050/INT-030).**
  Extract the endpoint lambda's orchestration into
  `NotificationRaiseService` (`Infrastructure/Application/Notifications/`,
  following the AuthService/ApiKeyService convention): store write inside a
  session, commit, **then** the fake email send — MOD-TXN-02 "never call
  external network inside a DB transaction" made structural. Endpoint HTTP
  behavior unchanged (same validation/400, policy/403, 201 body, same email
  strings).
- **S3 — ADR-003 "How tests prove it" table, A0-feasible rows**, new
  integration test file (real SQL Server; requirement IDs in test names per
  NFR-051):
  - **Outbox atomicity (OUT-01):** session → domain row + outbox row →
    forced failure → dispose-without-commit → assert 0 domain + 0 outbox.
  - **Two-context commit + rollback (MOD-TXN-04/05 — A0 analog of the
    GR/SO row):** commit persists both modules' rows; rollback leaves
    neither; post-session writes succeed on both contexts (restore proof).
  - **Single connection/transaction (MOD-TXN-01):** `ReferenceEquals`
    between both contexts' connections during the session.
  - **No external call in TXN (MOD-TXN-02):** `NotificationRaiseService`
    with a capturing session factory + spy email → assert the session is
    completed at every send (email never fires mid-transaction).
  - **Dispose-without-commit ⇒ rollback** (ADR-003 connection-lifetime
    row).
  - **Concurrent same-key idempotency (IDEM-05):** parallel
    `TryClaimAsync` same key+hash → exactly one `Claimed`, no exception,
    one row; fix the read-then-insert race at the root if the test
    exposes it (PK violation → re-read → accurate status).
  - **Production parity (NFR-050):** tests use the same
    `AmbientDbSessionFactory` class the Host DI registers, against real
    SQL Server; HTTP tests boot the same host composition — cited in the
    test file header.
- **S4 — adopt the session in `AuthService.CreateUserAsync`** (its own code
  comment defers `BeginTransactionAsync` to A0-6): `sp_getapplock` +
  revalidation + insert + audit become one ambient TXN; the existing A0-3
  create-user tests (incl. bootstrap-window and forbidden paths) are the
  regression net.
- **S5 — verification sweep:** `dotnet build` 0/0 → two consecutive green
  `dotnet test` → `dotnet format --verify-no-changes` → null-byte scan →
  HANDOFF sync.
- **S6 — close-out notes + HANDOFF sync;** remove throwaway
  `spike-tx-sql` (and scratch project) when the spike no longer needs it.

**Deferrals to record at close-out:** ADR-003 "Rollback multi-module R
(GR/SO confirm TXN)" → A2/A3 (GR/SO do not exist in A0; the two-context
rollback test is the A0 analog); "TD-10" → shipment slice; IDEM-04 HTTP
409/422 → first idempotency-key-consuming endpoint (no A0 endpoint accepts
keys); concurrent sessions within one scope → out of contract (sessions are
sequential per scoped request).

### A0-6 progress — S1–S3 core done (2026-11-18)

- **S1:** `AmbientDbSession` + `AmbientDbSessionFactory`
  (`src/Host/SupplyForge.Host/Data/`) — ADR-003 Option B: one shared
  `SqlConnection` + `DbTransaction`, `SetDbConnection` + `UseTransaction` on
  both contexts, idempotent cleanup on commit / rollback /
  dispose-without-commit, restore via fresh connection from the captured
  connection string (spike F6a–c), failed-begin unwind in the factory.
  Registered in Host DI; M0 `PlatformCore.Infrastructure/Data/DbSession.cs`
  deleted (zero call sites). Build 0/0.
- **S2:** `NotificationRaiseService` — session completes before the email is
  handed off (MOD-TXN-02); `POST /api/v1/notifications` delegates to it
  (400/403/201 behavior unchanged); scoped DI registration.
- **S3 (core):** `tests/PlatformCore.IntegrationTests/AmbientTransactionTests.cs`
  — `MOD_TXN_01` (both contexts share one connection), `OUT_01` (forced
  failure rolls back domain + outbox rows), `MOD_TXN_04` (commit persists
  both modules), `MOD_TXN_05` (rollback leaves neither; contexts usable
  after), `MOD_TXN_02` (email never fires mid-transaction), `IDEM_05`
  (concurrent same-key claims → exactly one claimed, single row) — 6/6
  green against real SQL Server using the production `AmbientDbSessionFactory`.
- **IDEM-05 defect found and fixed in-step:** `TryClaimAsync` raced
  read-then-insert on the PK (`DbUpdateException`); fixed by detaching the
  failed claim, re-reading `AsNoTracking`, and evaluating through a shared
  `EvaluateClaim(winner, payloadHash)` helper — regression-covered by the test.
- **S3 remainder (not written yet):** HTTP round-trip test — grant
  `platform.notification.raise` → login → `POST /api/v1/notifications` →
  201 → row verified in DB → cleanup; the only test resolving
  `NotificationRaiseService` + `AmbientDbSessionFactory` from the real Host DI.
- **Pre-close sweep (2026-11-18):** `dotnet build` 0 errors 0 warnings;
  `dotnet test` **147/147** (14 arch + 59 unit + 74 integration); `dotnet
  format --verify-no-changes` clean.
- **Not yet done:** S3 remainder, S4 (`AuthService.CreateUserAsync` adopts
  `IDbSessionFactory`), S5 final sweep, S6 close-out (incl. removing the
  throwaway `spike-tx-sql` container).
