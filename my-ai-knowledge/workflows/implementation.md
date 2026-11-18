# Implementation Workflow

Use only when modification is requested or authorized.

## 1. Prepare
- Read project instructions, relevant files, and the plan if one exists. Complete discovery first for unfamiliar code.
- Confirm scope and acceptance criteria. Check `git status` and preserve existing changes.

## 2. Implement
- Do only what the requirements need. No unrelated refactoring, cleanup, or features.
- Follow existing naming, structure, and patterns. Reuse existing components when they fit.
- Put each piece of code in the layer that owns it (see `AGENTS.md`).
- Work in small, coherent steps, each verifiable on its own. Keep the build working.
- Do not silently change public interfaces or behavior. Do not leave placeholders, TODOs, or temporary code.
- Handle edge cases, invalid input, and failures. Add dependencies only when clearly justified and compatible; prefer built-in framework features.
- Write or update tests alongside the change; use TDD when behavior is clear.

## 3. When something unexpected appears
- Stop and investigate. Do not work around it silently.
- If the finding exceeds the authorized scope, report it and ask.
- If the plan no longer fits, say so and adjust it with the user.

## 4. Before reporting done (checklist)
- [ ] Every requirement addressed; no extra scope.
- [ ] Consistent with project conventions and layer boundaries.
- [ ] No secrets, debug code, commented-out code, or unrelated edits (review the full diff).
- [ ] Input validated; authorization enforced where relevant.
- [ ] Tests added or updated; relevant build, tests, and linters actually run.
- [ ] Migrations, config, and docs updated where affected.
- [ ] Nothing committed, pushed, or deployed unless authorized.

Then follow `workflows/verification.md` and report: what changed, why, what was verified, what was not, and any open issues.
