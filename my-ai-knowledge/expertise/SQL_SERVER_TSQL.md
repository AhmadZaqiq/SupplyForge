# SQL Server / T-SQL Guidelines

Apply according to SQL Server version, workload, schema, and project conventions.

## Procedures and errors
- `SET NOCOUNT ON` in procedures and triggers unless row counts are part of the contract. Define parameters explicitly with proper types, lengths, and defaults.
- `SET XACT_ABORT ON` in transactional procedures. Use `TRY...CATCH` with `THROW` to preserve error context. Check `XACT_STATE()` in `CATCH` before commit or rollback. Do not rely on `@@ERROR`. Never continue as if a failed transaction were still usable.
- Read `@@ROWCOUNT` immediately after the statement.
- `RETURN` for status codes only when the project contract requires it; use result sets or output parameters for data.
- Treat `sp_helptext` and similar metadata tools as diagnostics, not runtime dependencies.

## Transactions
- Boundaries match one business operation with clear ownership. Document nesting behavior when a procedure may run inside an existing transaction. Do not hold transactions open across unrelated work or user interaction. Separate statements are not atomic without a transaction. Define rollback for every failure path.
- No `NOLOCK` as a generic fix for blocking; understand consistency trade-offs.

## Safe SQL
- Parameterize values (`sp_executesql`). Never concatenate untrusted input, even if it looks numeric. `QUOTENAME` is for identifiers, not values; allow-list dynamic identifiers and review the final statement.
- Do not expose sensitive data in diagnostic output, dynamic SQL text, or errors.

## Query writing
- Explicit column lists in `INSERT`; no `SELECT *` in procedures, views, APIs, or insert-select.
- Deterministic `ORDER BY` with a unique tie-breaker for `ROW_NUMBER`, `RANK`, `LAG`, `LEAD`, `OFFSET/FETCH`. Choose `ROW_NUMBER`, `RANK`, or `DENSE_RANK` by tie behavior. Consider keyset pagination for large or changing data.
- Handle `NULL` explicitly (it equals nothing, including `NULL`). Test empty sets and duplicates for aggregates.
- Use matching data types and explicit conversions; implicit conversions can change results and block index use. Be explicit about collation, time zone, precision, truncation, and invalid input. No locale-dependent date parsing.
- Initialize variables and give them explicit types.
- Set-based over loops and cursors. Use cursors or loops only for a demonstrated row-by-row need; document why, scope, cleanup, and volume.
- Temp tables for large, reused, or statistics-sensitive intermediate results; table variables only when their limits are acceptable. Compare with real workload evidence.
- CTEs are expressions, not materialized results. Bound recursive CTEs and set a `MAXRECURSION` policy.
- Prefer inline table-valued functions over multi-statement TVFs; scalar functions, MSTVFs, cursors, and triggers have non-trivial cost.
- Do not use `MERGE` without a project-approved pattern and careful concurrency testing.

## Triggers and integrity
- Handle multi-row statements via `INSERTED`/`DELETED` as sets. Test zero, one, and many rows. Prefer constraints and declarative integrity over triggers. Avoid recursive trigger side effects.
- Bulk operations that bypass validation or triggers need separate integrity checks.

## Performance and change safety
- Inspect actual execution plans before making performance claims. Review indexes, cardinality, locking, and transaction duration. When a plan changes unexpectedly, compare plans, statistics, parameters, indexes, and data distribution before rewriting.
- Migrations that change types, constraints, or indexes: assess locking, duration, rollback, and compatibility with older application versions.

## Minimum verification
For a material database change record: SQL Server version, affected objects, expected data impact, verification queries, rollback or recovery plan, and checks actually run. Test error paths, rollback, concurrency, duplicates, empty sets, and `NULL`. Never claim safety or performance without evidence.
