# SupplyForge — Scripts Documentation

## Overview

The `scripts/` directory contains three executable bash scripts for quota management and safe checkpoints. All scripts are designed to run on Ubuntu with bash, git, and OpenCode CLI available.

---

## 1. `quota-status.sh`

**Purpose:** Read OpenCode usage limits from the official endpoint (`GET /api/experimental/usage/limits`).

### Usage
```bash
scripts/quota-status.sh            # human summary
scripts/quota-status.sh --json     # raw endpoint response
QUOTA_WARN_PERCENT=70 scripts/quota-status.sh  # custom warning threshold
```

### Arguments
| Argument | Description |
|----------|-------------|
| `--json` | Output raw JSON response from endpoint |
| `QUOTA_WARN_PERCENT` env var | Warning threshold (default: 80%) |

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | Quota OK, below warning threshold |
| 10 | Warning — at or above threshold |
| 20 | Exhausted — a meter is limited |
| 3 | Unknown — no meters/budgets returned (fallback: usage-limit response) |
| 1 | Script error (missing dependency, unreadable response) |

### Prerequisites
- `opencode` CLI on PATH
- `jq` installed
- Signed-in OpenCode session (or `opencode service` running)

### Behavior
- Queries official OpenCode endpoint only — no guessing, no scraping
- On this machine: returns workspace `Personal` with no Go meters/budgets → exits 3 (UNKNOWN)
- Fallback trigger: usage-limit response from provider (HTTP 429 / "usage limit" error)

---

## 2. `checkpoint.sh`

**Purpose:** Safe pre-quota checkpoint — takes working tree from dirty to verified on GitHub in one guarded sequence.

### Usage
```bash
# Verification only (records result for later commit)
scripts/checkpoint.sh --verify-only [--verify-cmd "dotnet test"]

# Dry run (stages, inspects, stops before commit)
scripts/checkpoint.sh --dry-run --message "A1-4: warehouses + locations" -- <paths...>

# Full checkpoint (commit + push + verify)
scripts/checkpoint.sh --message "A1-4: warehouses + locations" -- <paths...>

# Skip push (local commit only)
scripts/checkpoint.sh --skip-push --message "..." -- <paths...>
```

### Options
| Option | Description |
|--------|-------------|
| `--verify-only` | Run verification only, record result, exit |
| `--dry-run` | Stage and inspect, but do not commit or push |
| `--message`, `-m` | Commit message (required for commit/dry-run) |
| `--verify-cmd` | Verification command (default: `dotnet build`) |
| `--no-verify` | Reuse recorded verification (max age: 30 min, configurable) |
| `--allow-failing-verify` | Checkpoint even if verification failed |
| `--allow-unchanged-handoff` | Skip HANDOFF change requirement |
| `--include-already-staged` | Include paths already staged before this run |
| `--skip-push` | Commit locally only; push pending |

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | Checkpoint VERIFIED (local SHA == remote SHA), or dry-run/verify-only done |
| 1 | Usage error / unsafe input (blanket path, nothing to do) |
| 2 | Verification failed (or stale/missing when `--no-verify`) |
| 3 | Quota information unavailable (informational; checkpoint still allowed) |
| 4 | Forbidden file would be committed (secret, credential, artifact, temp) |
| 5 | HANDOFF guard failed |
| 6 | Push or remote-SHA verification failed |
| 7 | Local repository is behind origin — resolve before pushing |

### Safety Guards
1. **No blanket staging** — refuses `-A`, `--all`, `.`, `*`, `:path` targets
2. **Forbidden files** — blocks `.env`, `secrets.json`, `*.pem`, `*.key`, `*.db`, `*.zip`, `bin/`, `obj/`, `.git/`, etc.
3. **Content scan** — blocks private keys, GitHub tokens, AWS keys in staged diff
4. **HANDOFF guard** — both `HANDOFF.md` and `docs/HANDOFF.md` must be updated and staged
5. **No force-push** — refuses if local branch is behind origin
6. **Remote SHA proof** — verifies `git ls-remote` matches local commit SHA

### Prerequisites
- Git repository with `origin` remote
- Both HANDOFF files tracked and committed
- Verification command available (`dotnet build` by default)

---

## 3. `checkpoint-selftest.sh`

**Purpose:** Regression tests for `checkpoint.sh` guards (19 cases in throwaway repositories).

### Usage
```bash
scripts/checkpoint-selftest.sh
```

### Arguments
None.

### Exit Codes
| Code | Meaning |
|------|---------|
| 0 | All 19 test cases passed |
| 1 | Any test case failed |

### Test Coverage
| # | Test Case |
|---|-----------|
| 1 | `.env` file refused |
| 2 | Blanket staging targets refused (`.`, `*`) |
| 3 | `--no-verify` without recorded verification |
| 4 | `--no-verify` with stale verification record (>30 min) |
| 5 | Private key content in staged diff refused |
| 6 | Only one HANDOFF file updated |
| 7 | Neither HANDOFF updated (and escape hatch `--allow-unchanged-handoff`) |
| 8 | Missing commit message |
| 9 | Happy dry-run stops before commit |
| 10 | `--skip-push` commits locally only |
| 11 | Full run with push — remote SHA equals local |
| 12 | Failed push reported as failure (not verified) |
| 13 | Archive files refused (`*.zip`, `*.tar`, etc.) |
| 14 | Local database files refused (`*.db`, `*.sqlite`, `*.bak`) |
| 15 | Build artifacts refused (`*.dll`, `*.pdb`, `*.exe`, `*.nupkg`) |
| 16 | OS junk refused (`.DS_Store`, `Thumbs.db`) |
| 17 | Git internals refused (`*/.git/*`) |
| 18 | Test result artifacts refused (`*.trx`, `*.coverage*`) |
| 19 | Temporary files refused (`*.log`, `*.tmp`, `*.swp`) |

### Prerequisites
- Bash, Git
- Write access to `$TMPDIR` (creates throwaway repos)
- `checkpoint.sh` in `../scripts/checkpoint.sh` relative to this script

### When to Run
- After any change to `checkpoint.sh`
- Before relying on checkpoint guards in production

---

## Common Patterns

### Pre-quota Checkpoint Workflow
```bash
# 1. Check quota
scripts/quota-status.sh

# 2. If warning/exhausted or usage-limit response:
scripts/checkpoint.sh --verify-only
# Update BOTH HANDOFF.md and docs/HANDOFF.md with results
# Agree exact path list with owner
scripts/checkpoint.sh --message "REQ-ID: description" -- <exact-paths...>
```

### Safe Development Loop
```bash
# Before substantial work
scripts/quota-status.sh

# During work (periodic)
scripts/checkpoint.sh --verify-only
```

---

## Environment Notes

- All scripts use `set -uo pipefail` for safety
- Paths are relative to repository root (`git rev-parse --show-toplevel`)
- Checkpoint artifacts stored in `.git/checkpoint/` (never committed)
- Scripts never modify application code, tests, or configuration