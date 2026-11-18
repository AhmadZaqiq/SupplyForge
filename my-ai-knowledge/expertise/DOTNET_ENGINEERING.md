# C# / .NET Engineering Guidelines

## Naming and code
- PascalCase for types, methods, properties, constants; camelCase for parameters and locals; `_camelCase` for private fields; `I` prefix for interfaces; `Async` suffix for async methods.
- Names are explicit: `CalculateOrderTotal`, `GetCustomerById`, `customerId`, `paymentRequest`. No `data`, `info`, `obj`, or abbreviations. Single letters only for conventional loop counters.
- Class names state responsibility: `OrderService`, `PaymentValidator`.
- Comments explain non-obvious reasoning, invariants, and trade-offs, not what the code says.
- Methods do one cohesive task. Extract when it clarifies; avoid tiny helpers that only add indirection. Many parameters → consider a parameter object. Many branches → simplify or separate without extra abstraction.
- Keep business rules out of infrastructure and presentation code. Make side effects explicit.

## Types and state
- Prefer immutability: `readonly`, `init`, records for DTOs and value objects.
- Make invalid states hard to represent: constructors/factories that validate, enums or dedicated types for restricted values.
- Encapsulate state; no public mutable setters unless needed. No magic numbers or unexplained string literals for business rules. No mutable global state.
- Enable nullable reference types and treat warnings as design feedback. Do not suppress analyzer warnings without understanding them.

## Errors and validation
- Validate input at application boundaries. Handle expected failures explicitly with useful context.
- Never swallow exceptions or use empty `catch`. Do not use exceptions for ordinary control flow.
- Catch only when you can recover, translate, or add context. Do not catch just to log and rethrow. Let unhandled exceptions reach a central handler (see `ASPNET_CORE_CONFIG_AND_LOGGING.md`).
- Do not return `null` or defaults to hide failure when the caller needs to know.
- Rethrow with `throw;`, never `throw ex;`.

## Async and resources
- Use async for I/O-bound work. Accept and pass `CancellationToken` through the whole call chain.
- Never block with `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`. No `async void` except event handlers. No `async` on purely synchronous methods.
- Avoid fire-and-forget; if unavoidable, own the lifetime, exceptions, cancellation, and observability.
- Use bounded concurrency for parallel I/O. Do not share non-thread-safe state (including `DbContext`) across concurrent operations.
- Dispose deterministically: `using` / `await using`. Make ownership explicit. Create `HttpClient` through `IHttpClientFactory`.
- Hosted background services need timeouts, cancellation, and graceful shutdown.
- Keep culture, time zone, and serialization explicit at boundaries; prefer `DateTimeOffset` and UTC; inject `TimeProvider` for testable time.

## Dependencies and framework
- Prefer built-in framework features over custom code or third-party packages. Before adding a package check maintenance, security, license, compatibility, and real value.
- Keep configuration external; validate required configuration at startup (options validation).
- Follow framework conventions when they improve consistency. Do not introduce abstractions to look sophisticated.

## Compatibility and maintenance
- Public contract changes need a backward-compatibility review and updates to consumers and tests.
- Measure before optimizing. Avoid unrelated refactoring.
- Define logging, metrics, health checks, and operational ownership for anything that runs outside a developer machine.
