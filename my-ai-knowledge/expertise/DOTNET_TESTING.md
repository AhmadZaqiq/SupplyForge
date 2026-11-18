# .NET Testing Guidelines

General rules are in `general/testing.md`. Follow the project's existing frameworks first; defaults below apply when none exist.

## Frameworks and conventions
- xUnit by default. One test project per production project (`X.UnitTests`, `X.IntegrationTests`).
- Name tests `Method_Scenario_ExpectedResult`. Arrange / Act / Assert, one behavior per test. Prefer `[Theory]` for input variations.
- Assertion libraries and mocking libraries: use what the project uses. Check licenses before adding (some versions of popular assertion and mocking libraries changed license terms).
- Mock only boundaries you own (e.g. `IEmailSender`, `IClock`). Do not mock `DbContext`, value objects, or the class under test.
- Time: inject `TimeProvider` (and `FakeTimeProvider` in tests). Randomness and GUIDs likewise behind an abstraction when tests depend on them.
- Build test data with builders or factories; avoid shared mutable fixtures.

## By layer
- **Domain:** pure unit tests of rules and invariants. No mocks.
- **Application:** unit test use cases with fakes or mocks for Infrastructure interfaces. Test validation, authorization decisions, and failure paths.
- **Infrastructure:** integration tests against the real database engine (SQL Server via Testcontainers, or the project's setup). The EF Core in-memory provider does not behave like a relational database; do not use it to verify queries, constraints, transactions, or concurrency. SQLite is also not equivalent to SQL Server.
- **API:** integration tests with `WebApplicationFactory<Program>`. Cover status codes, `ProblemDetails` shape, authentication (401), authorization (403, other user's resource), validation (400), and not found (404). Replace external services with fakes in the test host.

## Integration test rules
- Isolate state: fresh container or reset the database between tests (e.g. Respawn). No dependence on test order.
- Apply real migrations in test setup so the schema matches production.
- Keep them reasonably fast; run a small set of critical-path integration tests on every build.

## Architecture tests
- Enforce layer rules automatically (e.g. NetArchTest or ArchUnitNET): Domain references nothing; Application does not reference Infrastructure or API; controllers do not reference `DbContext`.

## Hygiene
- No `Thread.Sleep` or real network calls. Await async tests; never `async void`. Pass a `CancellationToken` in tests where the API accepts one.
- Treat flaky tests as defects. Never delete or weaken a test to make a change pass.
