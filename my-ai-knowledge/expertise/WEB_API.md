# ASP.NET Core Web API Guidelines

## Design
- Resource-oriented REST: nouns, consistent naming, correct HTTP semantics. GET is safe and never changes state. PUT is replacement; PATCH only for intentional partial updates. PUT and DELETE are idempotent.
- Controllers/endpoints are thin: bind, call the use case, map the result. No business logic or database access (see `CLEAN_ARCHITECTURE.md`).
- Use DTOs. Separate request and response DTOs when fields differ. Never expose entities. Return only the data clients need. Keep the contract independent of database shape.
- Use `[ApiController]` when its behavior fits; do not repeat automatic model validation. Declare response types (`ProducesResponseType`) and content types.
- Async for I/O with `CancellationToken`.

## Status codes
| Situation | Code |
|---|---|
| Created | 201 with location (`CreatedAtAction`) |
| Success without body | 204 |
| Malformed request or validation failure | 400 (`ValidationProblemDetails`) |
| Unauthenticated / forbidden | 401 / 403 |
| Not found | 404 |
| Method not allowed / unsupported media type / body too large | 405 / 415 / 413 |
| State conflict or concurrency conflict | 409 (or 412 with ETag) |
| Rate limited | 429 |

Do not return 200 for everything. Use one error shape (`ProblemDetails`) everywhere. Never expose stack traces, internal exceptions, SQL errors, or physical paths.

## Validation
- Validate every request on the server; never trust client validation.
- Validate collection sizes and handle empty collections. Validate pagination bounds.

## Files
- Enforce size limits and multipart limits. Allow-list formats and validate actual content, not the extension or client `Content-Type`.
- Generate server-side file names; never build paths from user names (path traversal). Store outside the web root and serve via controlled endpoints. Never return physical paths. Return correct media types for images and handle missing files.

## Contract and evolution
- Define versioning and deprecation before changing public contracts. Document supported versions and removal timeline.
- Collections: bounded pagination, validated filters, allow-listed sort fields. Never accept arbitrary field or expression names. Use keyset pagination for large datasets.
- Use ETag / `If-Match` for lost updates. Use idempotency keys for retryable state-changing operations.
- Define caching explicitly: cache-control, privacy, invalidation, authorization impact.
- Document authentication, errors, pagination, compatibility, and rate limits.

## Reliability
- Request timeouts, cancellation, response size limits, and rate limits at the boundary.
- Outbound calls: bounded timeouts, limited retries with backoff, handling of partial failure (e.g. Polly / `Microsoft.Extensions.Http.Resilience`).
- Return a correlation ID; emit structured telemetry without secrets or sensitive payloads.
- Test contract compatibility, malformed input, unauthorized access, concurrency conflicts, retries, and dependency failures.
