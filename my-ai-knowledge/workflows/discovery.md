# Discovery Workflow

Use for an unfamiliar or insufficiently understood project. Read-only: do not modify files.

## Steps
1. **Instructions first.** Read `AGENTS.md`, `CLAUDE.md`, README, CONTRIBUTING, docs, ADRs in the repo.
2. **Structure.** Identify solution/projects/modules, entry points, and where tests, config, scripts, and infrastructure live.
3. **Stack.** Languages, framework and runtime versions (from project files and lockfiles), package manager, key dependencies.
4. **Architecture.** Layers, dependency direction, composition root, cross-cutting concerns (auth, logging, errors, validation).
5. **Data.** Database engine, ORM, migrations, schema ownership, external integrations, queues, caches.
6. **Quality controls.** Build, test, lint, format, CI commands. Find the exact commands and confirm they run.
7. **Trace the relevant flow** end to end (request → layers → data) for the task at hand.
8. **Repo state.** Branch, `git status`, existing uncommitted work, recent history when relevant.
9. **Debt and risk.** Note constraints, fragile areas, and conventions that differ from the library's defaults.

## Rules
- Base every claim on files you read or commands you ran. Mark each finding as **verified**, **assumption**, or **unknown**.
- Do not infer architecture from folder names alone.
- Do not change code, install packages, or run migrations during discovery unless asked.

## Done when
You can state the stack, architecture, how to build and test, where the relevant code lives, and what remains unknown. Use `templates/discovery-report.md` if a written report is requested.
