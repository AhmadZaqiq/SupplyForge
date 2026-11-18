# Testing Standards

## Strategy
- Choose test level by risk and boundary: many fast unit tests, fewer integration tests, few end-to-end tests for critical journeys.
- Classify by resources: small (one process, no I/O), medium (one machine), large (multiple machines or external systems). Keep most tests small.
- Test observable behavior and business rules, not implementation details. Cover valid, invalid, boundary, and failure cases.
- Prefer state-based assertions over verifying internal interactions.

## Quality
- Tests are deterministic, independent, readable, and fail with a message that identifies what broke.
- No dependence on execution order, shared mutable state, arbitrary sleeps, or unstable external systems.
- Use realistic data. Assertions must be able to catch realistic defects; executing code without asserting is not testing.
- Use test doubles only when they add clear value. Never mock the behavior under test. Keep fakes consistent with real contracts.
- Coverage is an indicator, not proof. Do not impose arbitrary targets.

## Working with tests
- Inspect the existing setup before introducing new tools or conventions.
- Bug fix → add a regression test that fails before the fix.
- Consider TDD when behavior is clear: write a failing test, confirm it fails for the expected reason, make the smallest change to pass, refactor.
- Run the most focused tests first, then broaden.
- Investigate failures; separate product defects from test or environment problems. Treat flaky tests as defects.

## Integrity (hard rules)
- Never fabricate or assume test results. Report tests that could not run, and why.
- Never weaken assertions, skip, disable, or delete tests to make a change pass.
- Never change production behavior only to satisfy a flawed test.
- Do not modify unrelated code to get the suite green.
