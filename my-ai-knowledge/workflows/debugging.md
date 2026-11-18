# Debugging Workflow

## Steps
1. **Understand the failure.** Expected vs actual behavior, exact error, environment, when it started, what changed recently.
2. **Reproduce.** Find the smallest reliable reproduction. If you cannot reproduce it, say so and gather more evidence instead of guessing.
3. **Investigate systematically.** Read logs, stack traces, and the code path. Narrow the location by bisecting inputs, recent changes (`git log`, `git bisect`), or components.
4. **Hypothesize and test.** State one hypothesis at a time and test it with a targeted experiment. Keep notes of what was ruled out.
5. **Find the root cause.** Explain why the failure occurs, not only where. Distinguish cause from symptom and contributing factors.
6. **Fix.** Choose the smallest change that addresses the root cause. Check the same mistake elsewhere. Do not mask the problem with retries, broad catches, or null checks.
7. **Prove.** Add a regression test that fails before and passes after. Re-run the reproduction and related tests.
8. **Report.** Root cause, fix, evidence, residual risk.

## Rules
- Do not change many things at once. Do not edit tests to hide a failure.
- Preserve evidence (logs, repro steps) until the fix is verified.
- If the problem stays unresolved, report what is known, ruled out, and the next best step. Never claim a fix you did not verify.
- Use `templates/bug-report.md` when a written report is requested.
