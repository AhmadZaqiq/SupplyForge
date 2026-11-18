# EF Core Guidelines

EF Core is a query translator and change tracker, not a substitute for understanding the SQL. Verify features against the project's installed EF Core version.

## Queries
- Compose LINQ before materializing (`ToListAsync`, `ToArrayAsync`). Filter, sort, project, page, and aggregate in the database. Never load whole tables to filter in memory.
- Project with `Select` to DTOs when you do not need entities. Fetch only needed columns.
- Read-only: `AsNoTracking()` or projection. Use `AsNoTrackingWithIdentityResolution()` when repeated entity instances matter. `AsNoTracking` is not always faster; consider query shape.
- Existence: `AnyAsync()`, not `Count() > 0`. Single by key: `FindAsync` (may return a tracked instance, not a fresh query). `FirstOrDefault` when any match is fine; `SingleOrDefault` when uniqueness must hold. `First`/`Single` throw when empty; use them knowingly.
- `Include` only when related data is needed; use `ThenInclude` for deeper levels. Do not assume Include beats a join or projection. For multiple collection includes, check for Cartesian explosion; consider projection or `AsSplitQuery()` (more round trips, consistency trade-off; before EF Core 10, split queries with `Skip/Take` need fully unique ordering).
- Pagination: always bounded; deterministic order with a unique tie-breaker; keyset pagination for very large offsets.
- Stream large results with `AsAsyncEnumerable()` instead of buffering. Keep the `DbContext` alive and idle during enumeration.
- Do not assume every LINQ expression translates efficiently. Avoid accidental client-side evaluation. Inspect generated SQL (`ToQueryString()`, logging) when behavior or performance is unclear, before rewriting LINQ. `GroupBy` translation can surprise; inspect it.
- Prevent N+1: no navigation access in loops; use projection or proper loading. Understand what lazy loading will trigger before enabling it.

## DbContext and tracking
- Short-lived, scoped to one unit of work. Never share across requests. Never run parallel operations on one instance.
- Do not disable change detection globally without measuring. If `AutoDetectChangesEnabled` is off, detect changes before saving.
- Do not attach two instances with the same key to one context.

## Updates
- Ordinary update: load tracked entity, modify intended properties, save.
- Detached entities: control modified properties via `Entry()`. Avoid blind `Update()` on detached graphs: it can mark unintended properties and related entities as modified. Do not overwrite fields with missing or default request values.
- Group changes of one unit of work and call `SaveChangesAsync` once; never inside a loop. A single `SaveChanges` is atomic; separate calls are not. Use an explicit transaction when several operations must succeed together, and keep transaction ownership clear.
- `ExecuteUpdateAsync` / `ExecuteDeleteAsync` (EF Core 7+) for set-based changes: executed immediately, no tracking, not auto-grouped into one transaction, do not refresh already tracked instances.
- `AddRange`/`UpdateRange` are not inherently faster than individual calls.
- Pass `CancellationToken` to all async calls.

## Concurrency
- Configure optimistic concurrency (`rowversion` / concurrency token) where lost updates matter. Handle `DbUpdateConcurrencyException` deliberately: reject, reload, retry, or merge per requirements. Never silently overwrite.

## Schema and migrations
- Configure relationships, delete behavior, indexes, constraints, precision, and nullability explicitly when conventions are insufficient (Fluent API in `IEntityTypeConfiguration<T>` classes).
- Review every generated migration before applying: locking, data-transformation cost, rollback, compatibility with running app versions. Migrations are deterministic, with no environment-specific or destructive assumptions without approval.
- Database First scaffolding: keep custom code outside generated files so re-scaffolding does not overwrite it.
- Global query filters, interceptors, value converters, and shadow properties are hidden behavior: document and test them.
- Verify cancellation, timeout, retry, and transient-failure behavior against the actual provider. Use compiled queries only after measuring a real bottleneck.
- Integration-test important queries against a real database provider (not only the in-memory provider).
