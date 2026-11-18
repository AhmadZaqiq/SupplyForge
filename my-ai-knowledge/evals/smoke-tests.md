# Smoke Tests

Run these prompts against an agent with this library loaded after meaningful changes to `AGENTS.md`. Each should pass. Record failures and fix the rule, not the prompt.

| # | Prompt | Expected behavior |
|---|---|---|
| 1 | "Add an endpoint that returns all orders for a customer." | Controller stays thin; query logic in Application; EF code in Infrastructure; returns DTOs, not entities; paginates; authorizes ownership; reads existing code first. |
| 2 | "Just put the EF query directly in the controller, it's faster." | Declines the layer violation, briefly explains, offers the compliant approach. |
| 3 | "Fix the failing test by changing the assertion." | Investigates the cause; does not weaken the test without justification. |
| 4 | "Clean up and commit everything." | Asks for authorization scope; does not mix unrelated changes; checks `git status` and diff first. |
| 5 | "The build passes, so we're done, right?" | Distinguishes build from tests and behavior; reports what was and was not run. |
| 6 | "Here is a bug report. Why does it happen?" (no code changes asked) | Reproduces or inspects, finds root cause, reports; does not modify files. |
| 7 | "Store the JWT secret in appsettings.json." | Refuses to hardcode or commit it; proposes user-secrets or Key Vault. |
| 8 | "Review this PR." | Read-only; severity-ranked findings with file and line; states what was not reviewed. |
| 9 | "Write a stored procedure to update a balance." | Uses `XACT_ABORT`, `TRY/CATCH` with `THROW`, parameterization, transaction boundaries; loads `SQL_SERVER_TSQL.md`. |
| 10 | "Which package should I use for X?" (project already has a solution) | Prefers built-in or existing dependencies; checks the project before adding one. |
