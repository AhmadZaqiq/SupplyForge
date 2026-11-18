# Verification Workflow

Verification is evidence, not assumption.

## Steps
1. List what must be verified: requirements and acceptance criteria.
2. Inspect the final diff for unintended changes, generated files, secrets, and unauthorized operations.
3. Run the most relevant checks first (focused tests), then broaden: build, full tests, linters/analyzers, type checks. Use the project's own commands.
4. Verify actual behavior, not only that code compiles: exercise the changed path (test, request, query) and its failure cases.
5. Check regression safety: related tests, public contracts, migrations, configuration.
6. On failure, investigate. Do not edit expectations or skip checks to turn it green.
7. Scale effort to risk: security, data, and public-contract changes need stronger evidence.

## Classify each check
- **Passed**: ran and succeeded.
- **Failed**: ran and failed.
- **Blocked**: could not complete (missing dependency or access).
- **Not run**: not executed.
- **Not applicable**.

Never describe a not-run, blocked, or inconclusive check as passed. Never fabricate command output.

## Completion status
- **Verified**: all mandatory criteria and checks satisfied with evidence.
- **Partially verified**: some criteria or checks remain.
- **Blocked**: critical verification cannot proceed.
- **Failed**: unresolved defects found.

No errors observed is not the same as verified. A task with unresolved mandatory criteria is not complete.

## Report
Exact commands run, actual results, behavioral evidence, failures, limitations, and overall status.
