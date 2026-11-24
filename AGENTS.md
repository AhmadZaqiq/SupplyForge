# AGENTS.md — SupplyForge

## What this project is
Modular-monolith backend for the full goods lifecycle: supplier → purchase → receipt → stock → sale → shipment → invoice → payment → report. One company, three seeded branches. Portfolio backend, not a certified ERP.

## Shared workflow (OpenCode + Kilo)
- **One writer at a time.** The owner sets `Active agent:` in `docs/HANDOFF.md` §2. If it names another agent, stop.
- **No concurrent writes.** If the previous agent's inactivity cannot be verified (no handoff, stale handoff, or another agent may still be active), do not write. Wait for the owner to confirm the handoff.
- **Start of every session:** read `docs/HANDOFF.md` §2 "Current handoff", `docs/A1-PROGRESS.md`, `docs/Decision Register.md`; run `git status` and `git log --oneline -3`; verify the handoff against the repo.
- **Conflict rule:** if HANDOFF, Git, actual source, or the Decision Register disagree, stop and report the discrepancy. Never resolve it with assumptions.
- **Before stopping or switching:** refresh the full handoff record (files, decisions, build/test/format, migration status, git status, last commit), `docs/A1-PROGRESS.md`, and `docs/Decision Register.md`; set `Active agent: none`.
- **Owner decisions win.** `docs/Decision Register.md` overrides agent assumptions and old plans. Mark a superseded plan `SUPERSEDED` and link its replacement.
- **No commit, push, or migration apply** without separate explicit owner approval. Never redo verified work without checking the handoff and the repo first.

## Source authority (highest to lowest)
1. Latest explicit owner decisions — `docs/Decision Register.md` §8/§9 and owner instructions.
2. Accepted ADRs — `ADR/ADR-001…006, 008, 009`; govern architecture unless superseded.
3. PRD — `docs/prd/` (start `INDEX.md`, `00-how-to-use.md`); governs product requirements.
4. `docs/HANDOFF.md` — current operational state and exact next action.
5. `docs/A1-PROGRESS.md` — increment progress and verification evidence.
6. `docs/DATABASE-GUIDE.md` — verified database structure.
7. `README.md` — project overview and developer runbook.
8. Plans (`.kilo/plans/*`) are **never authoritative**.

## Stack
C#, .NET 10, ASP.NET Core Web API, SQL Server, EF Core 10, Docker Compose, xUnit with real SQL Server integration tests, OpenAPI, CI.
Not by default: microservices, Redis, RabbitMQ, MediatR, CQRS frameworks (PRD MOD-8; adding one needs an ADR).

## Where things are
| What | Where |
|---|---|
| Engineering rules + loading index | `my-ai-knowledge/AGENTS.md` (already loaded through `opencode.json`) |
| Workflows, general rules, expertise, templates | `my-ai-knowledge/`, opened on demand through that index |
| Requirements (authority) | `docs/prd/`, start at `docs/prd/INDEX.md` |
| Accepted decisions | `ADR/ADR-001` … `ADR-016`. ADR-017 … 019 are proposals, not decisions |
| Decision tracking | `docs/Decision Register.md` |
| Conceptual diagram | `SF.png` (conceptual, read-only) |
| Archived single-file PRD | `docs/prd/_archive/`. Never read it |

If `ADR/`, `docs/Decision Register.md` or a referenced ADR is missing, say so and ask. Do not invent the mechanism.

## Reading protocol (this keeps context small)
1. Any task that touches requirements: read `docs/prd/INDEX.md`, then `docs/prd/00-how-to-use.md`.
2. Identify the slice (A0 … B4). If the task does not say, ask.
3. Read the Core set and the slice files the INDEX lists. Nothing else unless a requirement ID sends you there.
4. One requirement: `grep -rn "INV-014" docs/prd/`, then read only the surrounding section. Grouped IDs: `docs/prd/appendix-c-id-aliases.md`.
5. Library files: open only the rows of `my-ai-knowledge/AGENTS.md` that match the task, with the Read tool.
6. Before writing code, list the files you read (PRD parts and library files).

## Architecture (differs from the library default)
Follow PRD §4 and MOD-1 … MOD-10, not the generic layer table in the library.
- Each module owns its tables. No module reads or writes another module's tables.
- Cross-module reads use public query contracts; writes use public commands or published events through the outbox in the same transaction.
- Only Inventory posts stock movements. Only Finance owns amounts owed/paid and credit exposure. Sales must never read Finance tables (MOD-9, SAL-074).
- Reporting is event-only and owns no operational writes (MOD-6).
- Solution and project layout is decided by the M0 ADRs (ADR-001 … 006). If they are not in `ADR/`, ask before creating projects or layers.
- When a library rule conflicts with the PRD or an ADR, the PRD or ADR wins. Say so in your answer.

## Scope and stop-and-ask
- Work only on the assigned slice. Never implement P2/P3, including AI (§5.12) and advanced integrations.
- Text marked `NEEDS OWNER APPROVAL`, an `OD-n` item, or a dependency on a proposed ADR: do not implement it. Ask the product owner.
- `Open-PO` question: implement the stated default (`TD-n` or the `Default` in §13.1) if there is one, otherwise ask.
- Invariants that always apply: §6.1, §6.3 (negative stock), §4.2, §4.3 (credit reservation), §4.5 (idempotency, transactions, outbox), BRN-011 … 014 (branch isolation, 404/403).
- Put the requirement ID in test names (NFR-051) and in commit or PR titles. Commit only when asked.

## Commands
```
Build:   dotnet build
Test:    dotnet test
Format:  dotnet format
Run:     docker compose up -d sqlserver && dotnet run --project src/Host/SupplyForge.Host
Migrate: [ask first] dotnet ef database update --project <module> --startup-project <host>
```

## Permissions
- May run: build, tests, formatters.
- Must ask first: migrations, package upgrades, commits, pushes, deployments, deleting data.

## Custom commands
- `/slice A2` starts a slice: reads the right PRD files, then proposes a plan and waits.
- `/req INV-014` looks up one requirement without reading whole files.
