# SOLID and Dependency Injection Guidelines

Apply for practical value, not mechanically.

## SOLID
- **SRP:** One cohesive responsibility and one reason to change. Not one method per class. Too many constructor dependencies signals mixed responsibilities; split the class rather than hiding dependencies behind a wrapper.
- **OCP:** Extend behavior through composition, strategies, or interfaces when variation is real. Do not modify stable code for every new case when a clean extension point exists.
- **LSP:** Derived types honor the base contract (preconditions, postconditions, invariants). Never `throw new NotImplementedException()` for an inherited member. If a subtype cannot honor the contract, drop the inheritance. Prefer composition unless it is a genuine "is-a".
- **ISP:** Small interfaces shaped by consumers. No empty or unsupported members.
- **DIP:** High-level policy depends on abstractions owned by the inner layer; infrastructure implements them. DIP (design principle) is not DI (technique).
- Create an interface only for real decoupling, multiple implementations, or a useful test boundary. Not one per class. Do not add factories, wrappers, or hierarchies without need.

## Dependency injection (.NET)
- Constructor injection for required dependencies. Use the built-in container unless a requirement justifies another.
- Register everything in the composition root, via extension methods per layer.
- No Service Locator, no resolving from `IServiceProvider` inside business code, no static access, no `BuildServiceProvider()` during registration. Never `new` a dependency that should be injected.
- **Lifetimes:** Transient for stateless and cheap; Scoped for per-request state (including `DbContext`); Singleton only if state, dependencies, and thread safety allow it.
- Never inject a Scoped service into a Singleton. Use `IServiceScopeFactory` in background services that need scoped services. Enable scope validation in Development.
- DI does not make a service thread-safe.
- For unit testing, inject abstractions; do not add interfaces solely for mocking when a real object works.
