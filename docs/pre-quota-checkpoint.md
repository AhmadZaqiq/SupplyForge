# Pre-quota checkpoint workflow

Purpose: **never lose work when the OpenCode free usage limit is approaching.**
This is a workflow, not application code — it changes no business behaviour and
no PRD requirement.

Authority: project `AGENTS.md` (commit/push need owner approval), library rules
6, 12, 13 (permission, honest verification, no weakened tests). Where those
conflict with this file, they win.

---

## 1. Where the quota number comes from

**Official source (verified 2026-11-19):**

```
opencode api GET /api/experimental/usage/limits
```

Operation ID `experimental.usage.limits`, described as *"Return the OpenCode Go
allowance and Console budgets of the OpenCode Console workspace the location is
signed in to, or null when it isn't signed in."*

Response shape:

| Field | Meaning |
|---|---|
| `.data.go.{rolling,weekly,monthly}.{percent,limited,resetsAt}` | Go allowance meters, `percent` 0–100, `limited` true when exhausted |
| `.data.budgets[].{limit,spent,resetsAt,...}` | Console budgets (USD) |
| `.data.workspace.{id,name,usageURL}` | workspace + link to the Console activity page |
| `.data = null` | location not signed in to a Console workspace |

**Current state of this machine:** signed in to workspace `Personal`, but the
endpoint returns **no Go meters and no budgets** (`budgets: []`). So there is
**no quota number to read**, and none is invented — see §3 fallback.

Local activity (sessions, tokens, prompts) is a *different* thing:

```
opencode stats          # usage history, not a quota
```

It never tells you whether a limit is near, so it is not used as a trigger.

---

## 2. Checking

```bash
scripts/quota-status.sh            # human summary
scripts/quota-status.sh --json     # raw endpoint response
QUOTA_WARN_PERCENT=70 scripts/quota-status.sh
```

| Exit | Meaning | Action |
|---|---|---|
| `0` | quota known, below threshold | normal work |
| `10` | quota known, ≥ `QUOTA_WARN_PERCENT` (default 80%) | **stop starting tasks**, checkpoint now |
| `20` | a meter is already `limited` | **quota exhausted**, checkpoint immediately, then stop |
| `3` | quota unknown | no number reported; §3 fallback applies |
| `1` | script error (dependency missing, bad response) | fix the script, do not assume OK |

Inside OpenCode: `/quota` runs the same check and reports the exit code.

---

## 3. Fallback when no quota number exists

Do **not** estimate a percentage from `opencode stats`, token counts, or time
of day. When `quota-status.sh` exits `3`, the trigger is the **usage-limit
response itself**: any HTTP 429 or a provider error containing a usage-limit
message received by the running session.

That response is the signal. On it: stop new tasks and go to §4.

---

## 4. The checkpoint sequence

Run in this order. Steps 1–5 are safe at any time; steps 6–8 need owner
approval because they commit and push.

1. **Quota** — `scripts/quota-status.sh`. Report the exit code.
2. **Stop** — no new tasks, no new slices, no speculative refactoring.
   Finish the current operation so the tree is at a consistent point (build
   compiles, no half-applied edit).
3. **Verify** — `scripts/checkpoint.sh --verify-only`
   (default command `dotnet build`, ~11 s incremental; override with
   `--verify-cmd "dotnet test ..."`). Record the exact result.
4. **Update both HANDOFF files** — `HANDOFF.md` **and** `docs/HANDOFF.md`,
   with: completed work, verification result, current state, exact next steps.
   The checkpoint script refuses to commit if either is missing, unstaged, or
   unchanged.
5. **Inspect** — `git status --porcelain` and the complete diff.
   `scripts/checkpoint.sh --dry-run ...` prints both and stages for inspection
   without committing.
6. **Agree the path list** with the owner. Exact files only — never `git add -A`.
7. **Commit and push** (owner approval required):
   ```bash
   scripts/checkpoint.sh --message "<title with requirement ID>" -- <path>...
   ```
8. **Read the report.** The checkpoint is only claimed as done when the report
   says `VERIFIED — remote SHA matches local commit`.

---

## 5. What `checkpoint.sh` refuses to do

| Guard | Behaviour |
|---|---|
| Blanket staging (`-A`, `--all`, `.`, `./`, `*`, `:/`) | exit 1, refuses |
| No paths given | exit 1, refuses |
| Paths already staged by something else | exit 1 unless `--include-already-staged` |
| `.env` (but not `.env.example`), `secrets.json`, `appsettings.*.local.json` | exit 4 |
| keys/certs (`*.pem`, `*.key`, `id_rsa*`), SQL data files (`*.mdf/ldf/ndf`), `*.db`, `*.sqlite`, `*.trx`, archives, `*.log`, `bin/ obj/ TestResults/ publish/ out/` | exit 4 |
| staged diff containing a private key, GitHub token (`ghp_…`) or AWS key (`AKIA…`) | exit 4 |
| either HANDOFF file missing, unstaged, or unchanged | exit 5 (unless `--allow-unchanged-handoff`) |
| merge/rebase/cherry-pick in progress, detached HEAD | exit 1 |
| local branch behind `origin` | exit 7 — pull/rebase with owner approval |
| `--no-verify` without a fresh recorded verification (< 30 min) | exit 2 |
| verification failed | exit 2 (unless `--allow-failing-verify`) |
| push | plain `git push`, never `--force`; success is claimed **only** after `git ls-remote` returns the local SHA |

Pre-existing work is never discarded: on a guard failure nothing is committed,
and nothing is reset, restored or deleted.

Artifacts of the last run live in `.git/checkpoint/` (never committable):
`last-quota.txt`, `last-verification.txt`, `last-verify-output.txt`,
`last-diff.txt`, `last-report.txt`.

---

## 6. After the checkpoint

```
CHECKPOINT VERIFIED — safe to stop here.
  local SHA == remote SHA == <sha> on origin/main
```

Resume from that commit:

```bash
git fetch origin && git status          # confirm you are on the checkpoint
```

then read `HANDOFF.md` for the state and next steps, run
`scripts/quota-status.sh`, and only then start substantial work.

**If the quota was exhausted mid-task:** nothing is lost. `git fetch origin`,
read the HANDOFF files, confirm `git status` is clean, and continue from the
"exact next steps" recorded in the last checkpoint. Never force-push or rewrite
history to "catch up" — pull instead.

---

## 7. Evidence

Verified on 2026-11-19, Ubuntu, `opencode v2.3.2`, git 2.53:

| Check | Result |
|---|---|
| endpoint exists in the server's OpenAPI document | yes — `experimental.usage.limits` |
| `quota-status.sh` against the live endpoint | `workspace: Personal`, exit **3** (no meters, no budgets) |
| `quota-status.sh --json` | raw payload matches the documented schema |
| `checkpoint.sh --help` | prints usage |
| `checkpoint.sh --verify-only` | quota snapshot printed, `verification: PASS` in 11.4 s, record written |
| `checkpoint.sh` dry-run and full run | see HANDOFF "status" row for this workflow |

Not tested: exit codes 10/20 (no meter data exists on this machine), exit 6/7
(push failure and behind-origin paths), because reproducing them would require
hitting a real limit or a diverged remote. Those branches are code-reviewed,
not executed.
