---
description: Look up one PRD requirement by ID (e.g. /req INV-014)
---
Look up requirement $ARGUMENTS in the SupplyForge PRD without reading whole files.

1. Search: `grep -rn "$ARGUMENTS" docs/prd/`. If nothing is found, check `docs/prd/appendix-c-id-aliases.md` for a grouped range.
2. Read only the section around each hit.
3. Report: the requirement text, its priority and slice, the invariants that apply, and any ADR, `TD-n`, `OD-n` or `Q-n` it depends on.
4. If it depends on a proposed ADR or is marked `NEEDS OWNER APPROVAL`, say so and do not propose an implementation.
Do not modify any file.
