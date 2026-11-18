# Clean Architecture Guidelines

Layers: **API → Application → Domain ← Infrastructure**. Dependencies point inward. Domain depends on nothing. If the project uses another structure, follow the project and mention the difference.

## Where code belongs

| Code | Layer |
|---|---|
| Entities, value objects, domain services, domain rules, domain exceptions/events, enums of business meaning | **Domain** |
| Use cases / handlers / application services, orchestration of a business workflow | **Application** |
| Request/response DTOs and mapping profiles, validators (FluentValidation or similar) | **Application** |
| Interfaces: `IOrderRepository`, `IEmailSender`, `IClock`, `ICurrentUser`, `IUnitOfWork` | **Application** (or Domain when it is a pure domain contract) |
| `DbContext`, entity configurations, migrations, repository implementations, query implementations | **Infrastructure** |
| External HTTP clients, email/SMS, file storage, queues, caching, identity-provider adapters | **Infrastructure** |
| Controllers/endpoints, HTTP binding, status-code mapping, `ProblemDetails`, auth attributes, middleware, `Program.cs` composition | **API** |
| DI registration | Each layer exposes `AddApplication()` / `AddInfrastructure()`; API composes them |

## Hard rules
- Domain references no other project and no framework or persistence package (no EF Core attributes, no ASP.NET types).
- Application references Domain only. It must not reference EF Core, `HttpContext`, or Infrastructure.
- Infrastructure implements Application/Domain interfaces. It must not contain business rules.
- API contains no business logic and no direct database access; controllers call a use case and map the result.
- Never return EF entities from the API. Map to DTOs.
- Define an abstraction in the inner layer that needs it; implement it in the outer layer.
- No circular project references. Verify direction in `.csproj` files.

## Judgment
- Do not add layers, repositories, CQRS, mediators, or mapping libraries without a concrete need. A repository over `DbContext` is justified when it hides real complexity or enables testing; otherwise using `DbContext` from Application-defined query interfaces may be simpler. Follow the project's existing choice.
- Prefer rich domain behavior over anemic entities when rules belong to the entity, but do not force DDD tactical patterns on simple CRUD.
- Use feature folders inside a layer when it improves cohesion.

## Review checks
- [ ] Each new class sits in the layer that owns its responsibility.
- [ ] No outward dependency was introduced (check `using` directives and project references).
- [ ] No business logic in controllers, EF configurations, or SQL procedures that duplicates domain rules.
- [ ] DTOs, not entities, cross the API boundary.
