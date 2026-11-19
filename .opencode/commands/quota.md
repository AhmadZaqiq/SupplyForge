---
description: Check the OpenCode usage limit before starting substantial work
---
Check the OpenCode usage limit for this project.

1. Run `scripts/quota-status.sh` (official endpoint `GET /api/experimental/usage/limits`).
2. Report the exit code and what it means:
   - `0` quota known, below the threshold — normal work.
   - `10` quota warning — do **not** start new tasks; offer `/checkpoint`.
   - `20` quota exhausted — checkpoint immediately, then stop.
   - `3` quota unknown — say plainly that no quota number is available. Do **not**
     estimate one from `opencode stats`, token counts or elapsed time; the
     fallback trigger is a usage-limit response from the provider.
3. If `10` or `20`: finish the current operation to a consistent point and run
   `/checkpoint`.
4. If `0` or `3`: continue with the task the owner asked for.

Do not modify any file. Do not touch billing, credits or usage limits.
