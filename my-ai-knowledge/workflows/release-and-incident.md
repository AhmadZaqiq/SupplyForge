# Release and Incident Workflow

## Release readiness
Decide **Ready / Ready with accepted risks / Not ready / Blocked**, with evidence for each item.

**Scope and quality**
- [ ] Release scope and version are clear (SemVer: breaking → major, feature → minor, fix → patch).
- [ ] Acceptance criteria verified; build and full tests pass in CI from a clean checkout.
- [ ] No unresolved critical or high findings; accepted exceptions are written down with an owner.

**Security and data**
- [ ] Security review done for sensitive changes; dependencies scanned; no secrets in artifacts.
- [ ] Migrations reviewed: locking, duration, compatibility with the running version, rollback or forward-fix plan, backup taken.

**Configuration and deployment**
- [ ] Required configuration, secrets, and environment differences documented and set.
- [ ] Deployment steps, rollback trigger, and rollback steps defined and tested where practical.
- [ ] Images tagged with immutable versions; no `latest` in production.

**Operations**
- [ ] Ownership and on-call clear. Liveness/readiness, logs, metrics, traces, and error reporting work.
- [ ] Alerts have owners, thresholds, and runbooks. Backup and restore are verified.
- [ ] High-risk changes have success metrics and rollback triggers; staged rollout or feature flag where blast radius is large.
- [ ] Changelog and user-facing documentation updated.

**After release**
- [ ] Deployed version and configuration recorded.
- [ ] Key user journeys, error rate, latency, and resources checked; rollback if thresholds are exceeded.
- [ ] Outcomes and follow-ups recorded.

## Incident response
1. **Detect and classify.** Confirm symptom, scope, start time, and impact. Name an owner and channel for significant incidents. Communicate an initial status early; do not wait for a perfect diagnosis.
2. **Stabilize first.** Protect users and data before finding the perfect root cause. Use rollback, feature flags, traffic reduction, or isolation. Prefer bounded, reversible actions; no destructive experiments; record who did what and why.
3. **Investigate with evidence.** Preserve logs, metrics, traces, deployment and config state. Compare before and after recent changes. Test one hypothesis at a time. Separate facts, hypotheses, and unknowns. Escalate suspected security or data incidents.
4. **Recover and verify.** Apply the smallest safe fix. Verify health, key journeys, data integrity, and error rates. Keep monitoring for recurrence.
5. **Follow up.** Blameless review: timeline, impact, detection, response, root cause versus contributing factors. Create corrective actions with owners and dates; add tests, alerts, runbook updates, or design changes.

Restore safe service quickly, preserve evidence, communicate honestly, and improve the system afterward.
