# SupplyForge — Plan Lifecycle Documentation

**Purpose:** Document the lifecycle and organization of plans in `.kilo/plans/`.

## Plan States

| State | Description | Files |
|-------|-------------|-------|
| **Active** | Current plan being executed or awaiting approval | At most 1 |
| **Superseded** | Replaced by a newer plan; kept for reference | Marked in HANDOFF.md |
| **Archived** | Historical; no longer relevant | Moved to `docs/_archive/plans/` if needed |

## Current Plans

| File | State | Description |
|------|-------|-------------|
| `1795541990951-a1-4-warehouses-locations.md` | **Superseded** | Original A1-4 plan; replaced by `1795543141178-a1-4-warehouses-locations-plan.md` |
| `1795543141178-a1-4-warehouses-locations-plan.md` | **Active** | A1-4 warehouses + locations implementation plan |
| `1795543141178-knowledge-handoff-audit.md` | **Active** | Documentation sync + handoff protocol plan |
| `1795543141178-reconciliation-report.md` | **Superseded** | Reconciliation evidence; folded into A1-PROGRESS.md |
| `1795543141178-shared-workflow-protocol.md` | **Active** | Shared workflow protocol documentation |
| `1795548582068-repository-organization-maintainability-audit.md` | **Active** | This audit plan (Category A items approved) |

## Naming Convention

`{timestamp}-{slug}.md` where timestamp is Unix epoch milliseconds.

## HANDOFF References

HANDOFF.md §2 explicitly references plan files by path (e.g., superseded plan). Do not delete or move plans without updating HANDOFF references.

## Git Tracking

Plans are **tracked in Git** (not gitignored) to preserve handoff context in fresh clones. No staging or commit without explicit owner approval.

## Archival Process

When a plan is fully superseded and no longer referenced:
1. Verify no HANDOFF or documentation references remain
2. Move to `docs/_archive/plans/` (create if needed)
3. Update this README