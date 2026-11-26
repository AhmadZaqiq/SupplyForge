# Remediation Plan — SupplyForge Audit Response

Created: 2026-11-26. Companion to `REMEDIATION-REGISTER.md` (finding IDs) and
`FINAL-REMEDIATION-REPORT.md` (outcomes). This plan defines execution order,
dependencies, risks, and completion gates. It is an operational document, not a
requirements source; PRD + accepted ADRs + `docs/Decision Register.md` remain
authoritative.

## Scope boundaries (hard)

- **In scope:** verified defects in solution/test wiring, documentation drift,
  one already-designed-but-unimplemented A1 deliverable (MD-042 guard, design
  D2), test-parallelism hardening, and two proposal documents (outbox
  dispatcher, first business API increment + permission matrices).
- **Out of scope:** A1-8, A1-9, A2+, migrations applied to any existing
  database, commits/pushes, new NuGet packages, permission-code seeding,
  blocked requirements (INV-041, SEC-40, WAC-07, Q-C, PLT-013), security
  changes that alter accepted ADRs (PBKDF2) or require schema changes
  (refresh-token concurrency), and destructive cleanup (leftover databases).
- Only files under `docs/audit-remediation/` are new; existing files are
  edited only where a verified defect exists.

## Execution order, dependencies, gates

| # | Batch | Depends on | Work | Gate before continuing |
|---|---|---|---|---|
| 1 | Baseline verification | — | Re-verify every audit finding against `29d65ba` (register created) | Register exists; each finding classified with evidence |
| 2 | **A — solution/test coverage** | 1 | Add both Inventory test projects to `SupplyForge.slnx` | `dotnet build` at root compiles 7 test projects; `dotnet test` at root executes all 384 tests |
| 3 | **G — test reliability** | 2 | `xunit.runner.json` (serialize collections) in the three integration test projects + csproj content copy | Two consecutive full root runs green; no test removed or weakened |
| 4 | **B — documentation integrity** | 1–2 (test counts must be real) | README (location types, StockBalance key, stale commit rows), `DATABASE-GUIDE` (40 tables, new Inventory section), `A1-PROGRESS` (A1-5 deferral wording, A1-7 counts) | Every changed claim re-checked against code/migrations/git; `grep` finds no stale phrasing |
| 5 | **C — MD/Inventory deliverable** | 1 | Implement Inventory `LocationDeactivationGuard` (design D2) + Host wiring + tests | New requirement-ID tests pass; placeholder no longer registered; full suite green |
| 6 | **D/E — proposals** | 1 | `OUTBOX-DISPATCHER-PROPOSAL.md`, `API-INCREMENT-PROPOSAL.md` (incl. permission matrices, first endpoints, DTOs, tests, dependencies) | Proposals cite PRD IDs; no code, no seeded permission codes |
| 7 | **F/H — classification only** | 1 | Security/infra findings validated vs HANDOFF §12 and ADRs; blocked items recorded | Each has status + prerequisite; no unauthorized change |
| 8 | Final verification | 2–5 | `dotnet build`, full `dotnet test`, Inventory test projects, `dotnet format`, `git diff --check`, checkpoint self-test, fresh-diff review | All executed; counts recorded; diff reviewed for scope creep |
| 9 | Handoff + report | 8 | Refresh `docs/HANDOFF.md` §2 (workstream B + AGENTS.md handoff rule); write `FINAL-REMEDIATION-REPORT.md` | Handoff matches git; report lists every file changed |

## Risks

| Risk | Mitigation |
|---|---|
| Adding Inventory tests to the root run exposes the known parallelism timeouts (D0) | Batch 3 caps xUnit parallelism *before* relying on the full suite; runs repeated |
| Serializing integration collections lengthens the suite | Only the three integration projects are capped; unit/architecture keep defaults; measure wall time |
| MD-042 guard wiring changes DI resolution for every Host test | Wire in `ServiceCollectionExtensions` exactly where the placeholder sits; run the full HTTP/Host test set |
| Documentation edits introduce new drift | Every edited claim is traced to code/migrations/git in this session; no numbers copied from the audit without re-counting |
| Scope creep into A1-8/A2 | Register marks each deferred item with its prerequisite; no business increments started |

## Completion gates (project-level)

1. All CONFIRMED findings in the register are fixed and verified by executed commands.
2. No file outside the authorized list is touched; `opencode.json` pre-existing modification preserved.
3. No migration applied, no database dropped/modified, no commit/push.
4. Blocked items each carry an explicit owner decision request.
5. Final report states executed commands, exit codes, and per-project test counts.
