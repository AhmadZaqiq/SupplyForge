<!-- SupplyForge PRD v0.8 (AI edition) · verbatim extract of §7 · nothing changed · navigation: docs/prd/INDEX.md -->

## 7. External integrations

| Integration | Stage stance |
| --- | --- |
| Email | P1A: adapter + fake; real send if configured (TD-2); manual mark-sent fallback |
| Carrier | P1A: manual path full; automated adapter P2 |
| Payment provider | P2; P1B manual payment entry + allocation |
| AI providers | Not P1 |
| Outbound webhooks | P2 |
| Client API | API keys + scopes P1A (INT-050) |

- **INT-001 (P1A)** Adapter interface + fake per external category used.
- **INT-002 (P1A)** Outbound from queued jobs fed by outbox; **never inside DB transaction**.
- **INT-003 (P1A)** Retry exponential backoff + jitter; dead-letter visible.
- **INT-004 (P1A)** Timeouts; simple circuit breaker OK P1.
- **INT-005 (P2)** Webhooks: signature, replay window, store raw first, once per provider event id. (No P1 inbound webhook source exists; see ADR-011.)
- **INT-006 (P1A)** Integration log: status, duration, correlation id, redacted payload.
- **INT-007 (P1A)** Credentials encrypted; never in API/logs.
- **INT-008 (P1A)** Health view: last success, failures, queue depth.
- **INT-009 (P1A)** Respect provider rate limits.
- **INT-010–013 (P2)** Payment provider link/events/refunds/reconciliation.
- **INT-020–022 (P2)** Carrier adapters; P1 manual carrier works without integration.
- **INT-030–031 (P1A)** Email queue when configured; bounce flag P2.
- **INT-040–041 (P2/P3)** AI provider abstraction.
- **INT-050 (P1A)** API keys with scopes; revoke. **Home: A0 (ADR-009).**
- **INT-051 (P2)** Outbound webhooks.
- **INT-052 (P1A)** OpenAPI from code.

Failure philosophy: business commit never depends on external success; effects eventually run or surface as failed jobs.

---
