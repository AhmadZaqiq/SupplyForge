# ASP.NET Core Configuration, Logging, and Error Handling

Version notes: `IExceptionHandler` and `AddProblemDetails()` require .NET 8+. Verify against the project's target framework.

## Configuration
- Bind settings to strongly typed options (`IOptions<T>`; `IOptionsSnapshot<T>` or `IOptionsMonitor<T>` when values must change at runtime). Validate on startup: `AddOptions<T>().BindConfiguration(...).ValidateDataAnnotations().ValidateOnStart()`. Fail fast on missing required configuration.
- Do not read `IConfiguration` directly inside business logic.
- Sources by precedence: `appsettings.json` (non-sensitive defaults) → `appsettings.{Environment}.json` → environment variables (`Section__Key`) → secrets. Development secrets in user-secrets, never committed. Production secrets in Key Vault or the platform's secret store.
- Do not branch behavior on environment names in domain code; keep environment differences in configuration.

## Error handling
- One central handler: `AddProblemDetails()` + an `IExceptionHandler` that maps known exceptions to `ProblemDetails` and returns a generic 500 for unknown ones, with a trace/correlation ID. No stack traces or internal messages in responses outside Development.
- Expected failures (validation, not found, conflict) flow through explicit results or domain exceptions mapped to 400/404/409, not generic 500s.
- Log the exception once, at the boundary that handles it.

## Logging
- Inject `ILogger<T>`. Use message templates with named placeholders: `_logger.LogInformation("Order {OrderId} created", orderId)`. Never string interpolation or concatenation, which breaks structure and allocates.
- For hot paths use the `[LoggerMessage]` source generator.
- Levels: Trace/Debug diagnostics; Information for significant business events; Warning for recoverable anomalies; Error for failed operations; Critical for service-threatening failures. No Information-level noise in loops.
- Never log secrets, tokens, passwords, full request bodies, or unnecessary personal data. Sanitize user-controlled values. Do not log and rethrow the same exception at every layer.
- Include correlation: use logging scopes with request or trace ID. Return the ID to the client in error responses.
- Structured sinks (e.g. Serilog) or OpenTelemetry exporters are acceptable; follow the project's existing choice.

## Health checks and telemetry
- Separate endpoints: **liveness** (process is up, no dependency checks) and **readiness** (database and critical dependencies reachable), using `AddHealthChecks()` with tags and `MapHealthChecks`. Keep checks fast and bounded. Do not expose detailed health output publicly.
- Use OpenTelemetry for traces and metrics (ASP.NET Core, `HttpClient`, EF Core/SQL instrumentation) when the project needs distributed diagnostics. Track request rate, errors, latency, and key business counters.

## Hosting and shutdown
- Handle `IHostApplicationLifetime` and cancellation tokens in background services; finish or checkpoint in-flight work on shutdown. Configure the host shutdown timeout to fit the longest operation.
- Run behind a reverse proxy with forwarded headers configured and `UseHsts` / HTTPS redirection as appropriate.
