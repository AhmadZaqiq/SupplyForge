# Observability and Operations Standards

For .NET specifics see `expertise/ASPNET_CORE_CONFIG_AND_LOGGING.md`.

## Operability
- Define how the service starts, becomes ready, shuts down, and recovers. Document configuration, dependencies, capacity assumptions, and ownership.
- Make failure modes explicit and prefer safe, recoverable behavior.

## Logs, metrics, traces
- Structured logs: timestamp, severity, component, correlation/request ID, outcome.
- Metrics for availability, latency, traffic, errors, saturation, and key business outcomes. Tracing across service and dependency boundaries.
- Every signal must answer a question. Never log secrets, tokens, unnecessary personal data, or full request bodies by default.

## Health and alerting
- Separate liveness from readiness. Health checks are bounded, cheap, and representative.
- Alert on user-impacting, actionable conditions. Each important alert has an owner, severity, and runbook. Test alert delivery.

## Reliability and recovery
- Timeouts, bounded retries, backoff, and jitter for external calls. Retries only for idempotent or deduplicated operations.
- Prevent retry storms and cascading failure: limits, circuit breaking, queues, load shedding.
- Define backup, restore, rollback, and disaster-recovery expectations, and test them. An untested recovery plan is an assumption.

## Change discipline
- Use gradual rollout or feature flags for high-blast-radius changes. Define success metrics and rollback triggers beforehand.
- Record limitations and unresolved risks honestly.

## Rule
A system is production-ready only when it is observable, supportable, and recoverable, not merely built and tested.
