---
description: Run the safe pre-quota checkpoint (verify, inspect, commit, push, prove)
---
Run the pre-quota checkpoint for SupplyForge. Optional argument: a requirement
ID or short note to put in the commit title.

1. Run `scripts/quota-status.sh` and report its exit code. If it is `3`, say
   that no quota number is available and that the usage-limit response is the
   fallback trigger — do not guess a percentage.
2. Stop starting new tasks. Bring the current operation to a consistent point
   (nothing half-edited, project still builds). Do not start anything new.
3. Run `scripts/checkpoint.sh --verify-only` and read the result. This is the
   fastest relevant verification; use `--verify-cmd "dotnet test ..."` only if
   the owner asks for tests instead.
4. Update **both** `HANDOFF.md` and `docs/HANDOFF.md` with: completed work, the
   verification result, current state, and exact next steps for resuming. The
   checkpoint refuses to run if either file is unstaged or unchanged.
5. Show `git status --porcelain` and the complete diff, and agree the **exact**
   path list with the owner. Never `git add -A`. Never include `.env`,
   `secrets.json`, databases, `bin/ obj/`, archives or `SupplyForge.zip`.
6. Dry run first:
   `scripts/checkpoint.sh --dry-run --message "<title with the requirement ID>" -- <paths>`
   and review what it says it would commit.
7. Committing and pushing need explicit owner approval (project `AGENTS.md`).
   After approval, re-run the same command without `--dry-run`.
8. Report the checkpoint from the script's output: verification result, quota,
   local SHA, remote SHA (they must match), files committed, what is still
   dirty, and the next steps. Only say "pushed" when the report says
   `VERIFIED — remote SHA matches local commit`.

Never force-push, never rewrite history, never discard existing work, and never
claim a push you did not verify.
