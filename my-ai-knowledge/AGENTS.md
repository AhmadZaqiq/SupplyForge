# AGENTS.md — Always-On Rules

These rules apply to every task. They are short on purpose. Detailed guidance lives in the files listed in the index below; open only what the task needs.

> **Library root:** `/home/ahmad/SupplyForge/my-ai-knowledge/`. Every path in the tables below is relative to this root.
> Path mentions are not auto-imports. When a task matches a row, open that file with the Read tool before you start, and say which files you read.

## Non-negotiable rules

### Architecture (Clean Architecture: API → Application → Domain ← Infrastructure)
1. Code goes in its own layer only. Never place code in a layer that does not own it.
   - **Domain**: entities, value objects, domain rules. No references to any other layer, EF Core, HTTP, or frameworks.
   - **Application**: use cases, orchestration, DTOs, validators, interfaces for infrastructure (repositories, external services). No EF Core, no HTTP types.
   - **Infrastructure**: EF Core, DbContext, repositories, external clients, file/email/queue implementations.
   - **API**: controllers/endpoints, HTTP mapping, auth wiring, DI composition. No business logic, no direct database access.
2. Dependencies point inward only. Inner layers never reference outer layers. No circular references.
3. Never expose EF entities through the API. Use request/response DTOs.
4. If the project uses a different architecture, follow the project's architecture and say so.

### Safety and scope
5. Do only what was asked. No unrelated refactoring, renaming, formatting, or "improvements".
6. Never commit, push, deploy, delete data, or run destructive commands (reset, clean, force-push, drop, `down -v`) without explicit permission.
7. Preserve existing uncommitted work. Check `git status` before editing.
8. Never hardcode or log secrets, tokens, passwords, or personal data.
9. Treat all external input as untrusted: validate on the server, use parameterized queries, enforce authorization on every protected operation, including resource ownership.

### Honesty and verification
10. Read the existing code and project instructions before changing anything. Follow existing conventions over personal preference.
11. Do not invent APIs, packages, files, requirements, or results. If unsure, check the code or documentation, or ask.
12. Run the relevant build, tests, and linters after changing code. Never claim a check passed unless you ran it. Report what was run, what failed, and what was not run.
13. Fix the root cause, not the symptom. Never weaken, skip, or delete tests to make them pass.
14. Ask one focused question when a missing answer would materially change the solution. Otherwise state your assumption and continue.

### Code quality
15. Choose the simplest solution that meets the requirement. No speculative abstractions, layers, or packages.
16. Names are explicit and meaningful. Methods have one responsibility. No empty `catch`, no `.Result`/`.Wait()`, always propagate `CancellationToken`.
17. Any behavior change needs a test; any bug fix needs a regression test.

## Communication
- Assume the developer is early-career with solid fundamentals: explain trade-offs briefly, skip beginner tutorials.
- Keep answers short and concrete. Lead with the result, then the evidence.
- Mention any material conflict between these rules and the project's rules.

## How to load more guidance
Open a file only when the task matches it. Do not load the whole library. Never claim to follow a standard you did not read.

| Task or signal | Read |
|---|---|
| Unfamiliar project | `workflows/discovery.md` |
| Complex feature or architectural change | `workflows/planning.md` |
| Writing or changing code | `workflows/implementation.md` |
| Bug or failing test | `workflows/debugging.md` |
| Reviewing code or a security review | `workflows/review.md` |
| Checking that work is really done | `workflows/verification.md` |
| Release, deployment, or production incident | `workflows/release-and-incident.md` |
| Writing tests | `general/testing.md` and `expertise/DOTNET_TESTING.md` |
| Security-sensitive change | `general/security.md` |
| Git operations or commits | `general/git-workflow.md` |
| Logging, health checks, reliability | `general/observability-and-operations.md` and `expertise/ASPNET_CORE_CONFIG_AND_LOGGING.md` |
| Design judgment, readability, refactoring | `general/principles.md` |
| Needs a structured document | matching file in `templates/` |

### Expertise (the developer's own stack; load when the technology is involved)

| Technology or signal | Read |
|---|---|
| Any C#/.NET code | `expertise/DOTNET_ENGINEERING.md` |
| Layers, project structure, where code belongs | `expertise/CLEAN_ARCHITECTURE.md` |
| Interfaces, DI, service lifetimes | `expertise/SOLID_AND_DI.md` |
| Controllers, endpoints, HTTP contracts | `expertise/WEB_API.md` |
| Auth, JWT, passwords, CORS, rate limiting | `expertise/API_SECURITY.md` |
| EF Core, migrations, DbContext | `expertise/EF_CORE.md` |
| SQL Server, T-SQL, stored procedures | `expertise/SQL_SERVER_TSQL.md` |
| Docker, Compose, images | `expertise/DOCKER.md` |
| xUnit, integration tests, test data | `expertise/DOTNET_TESTING.md` |
| Configuration, secrets, logging, health checks, error handling middleware | `expertise/ASPNET_CORE_CONFIG_AND_LOGGING.md` |

## Precedence
1. The user's explicit request and safety constraints.
2. The target project's instructions, conventions, and configuration.
3. These rules, then the smallest applicable set of library files.

## Maintenance
Keep this file under about 100 lines. Add a rule only for a real recurring risk. Update the index when adding or renaming a file. Run `evals/smoke-tests.md` after meaningful changes.
