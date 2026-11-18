# Planning Workflow

Use for complex features, architectural changes, risky migrations, or when the approach is unclear. Skip for small, obvious changes.

## Steps
1. **Requirements.** Restate the goal, constraints, and acceptance criteria. Separate requirements from assumptions. Ask only questions that change the plan.
2. **Context.** Read the affected code, patterns, contracts, and data. Reuse what exists.
3. **Options.** For non-trivial decisions compare at least two approaches by fit with the existing project, complexity, risk, and cost. Choose the simplest that works and say why.
4. **Scope.** State what changes and what explicitly does not. Identify affected layers, public contracts, and data.
5. **Risks and dependencies.** Breaking changes, migrations, security, performance, concurrency, rollout and rollback.
6. **Steps.** Ordered, small, independently verifiable tasks. Each step names the layer and files it touches.
7. **Verification.** Testable acceptance criteria and the exact checks that will prove them.
8. **Review the plan.** Every step serves the goal; nothing unrelated; no unnecessary complexity; criteria are testable.

## Boundaries
- A request for a plan is not authorization to implement. In planning-only mode, do not modify project files.
- Wait for approval when the user asked for it or the change is architectural, destructive, or a significant migration.
- If new facts invalidate the plan, update it and tell the user before continuing.
- Keep the plan proportional to the task. Use `templates/implementation-plan.md` for a formal plan and `templates/decision-record.md` for significant decisions.
