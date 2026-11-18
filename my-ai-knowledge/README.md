# AI Engineering Knowledge Base

Reusable standards, workflows, templates, and personal .NET expertise for AI coding agents.

## Use it
1. **Every task:** `AGENTS.md` holds the always-on rules and the index. Agents that read `AGENTS.md` pick it up automatically. For any other chat or tool, paste the contents of `AGENTS.md` at the start of the prompt.
2. **Each project:** copy `templates/project-AGENTS.md` into the project root and fill in the commands, architecture, and gotchas.
3. **On demand:** the agent opens only the files the index points to. Path mentions are not auto-imports; the agent must read them. Some tools need the library path configured or the files copied into the repository.
4. For Claude Code, if a project has no `CLAUDE.md`, current versions read `AGENTS.md`; otherwise import it from `CLAUDE.md`.

## Layout
```text
AGENTS.md     Always-on rules + loading index (keep under ~100 lines)
general/      Technology-independent standards
workflows/    Task procedures, each with its own completion checklist
templates/    Output formats and the per-project AGENTS.md
expertise/    The developer's own stack: C#/.NET, Clean Architecture, Web API, EF Core, SQL Server, Docker, testing
evals/        Smoke tests to check the library still steers agents correctly
SOURCES.md    Basis for the standards
```

## Design rules
- One responsibility per file; update the existing rule instead of duplicating it.
- Put a rule here only if the repository cannot reveal it or it prevents a recurring mistake.
- Project-specific facts belong in the project, not here.
- This library does not replace tests, project configuration, or human authorization.
