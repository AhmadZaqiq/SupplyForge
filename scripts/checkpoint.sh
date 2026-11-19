#!/usr/bin/env bash
#
# checkpoint.sh — safe pre-quota checkpoint for SupplyForge.
#
# Purpose: never lose work when the OpenCode free usage limit is approaching.
# It takes the working tree from "dirty" to "verified on GitHub" in one
# repeatable, guarded sequence:
#
#   1. snapshot OpenCode quota from the official endpoint (unknown is reported,
#      never guessed) and record it in the report
#   2. run the fastest relevant verification and record the exact result
#   3. print `git status --porcelain` and the complete diff
#   4. stage ONLY the paths given on the command line — never `git add -A`
#   5. refuse secrets, credentials, local databases, build artifacts and temp files
#   6. refuse to commit unless BOTH HANDOFF files are updated and staged
#   7. commit, push (never force), and prove the remote SHA equals the local SHA
#   8. print a checkpoint report with exact next steps
#
# Usage:
#   scripts/checkpoint.sh --verify-only
#       Run step 2 only and record the result (so it can be written into the
#       HANDOFF files before the commit).
#
#   scripts/checkpoint.sh --message "<title with requirement ID>" -- <path>...
#       Full checkpoint of exactly the paths listed after `--`.
#
#   scripts/checkpoint.sh --dry-run --message "..." -- <path>...
#       Everything up to and including staging + guards, then stop before the
#       commit. Nothing is committed or pushed.
#
# Options:
#   --message, -m <text>       commit message (required for commit/dry-run)
#   --verify-cmd <cmd>         verification command (default: dotnet build)
#   --no-verify                reuse a recorded verification instead of running one
#   --allow-failing-verify     checkpoint even though verification failed
#   --allow-unchanged-handoff  skip the "HANDOFF files must change" requirement
#   --include-already-staged   commit paths that were already staged before this run
#   --skip-push                commit locally only; the report says push is pending
#   --dry-run                  stage and inspect, but do not commit or push
#   --verify-only              run verification, record it, exit
#
# Exit codes:
#   0  checkpoint verified (local SHA == remote SHA), or dry-run/verify-only done
#   1  usage error / unsafe input (blanket path, nothing to do)
#   2  verification failed (or stale/missing when --no-verify)
#   3  quota information unavailable (informational; checkpoint still allowed)
#   4  a forbidden file would be committed (secret, credential, artifact, temp)
#   5  HANDOFF guard failed
#   6  push or remote-SHA verification failed
#   7  local repository is behind origin — resolve before pushing
#
set -uo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel 2>/dev/null)" || { echo "checkpoint: not inside a git repository" >&2; exit 1; }
GIT_DIR="$(git -C "$REPO_ROOT" rev-parse --absolute-git-dir)"
CKPT_DIR="$GIT_DIR/checkpoint"
mkdir -p "$CKPT_DIR"

SCRIPT_QUOTA="$REPO_ROOT/scripts/quota-status.sh"
HANDOFF_FILES=("HANDOFF.md" "docs/HANDOFF.md")

MODE="full"
DRY_RUN=0
SKIP_VERIFY=0
SKIP_PUSH=0
ALLOW_FAILING_VERIFY=0
ALLOW_UNCHANGED_HANDOFF=0
INCLUDE_ALREADY_STAGED=0
MESSAGE=""
VERIFY_CMD="${SF_VERIFY_CMD:-dotnet build}"
VERIFY_MAX_AGE="${SF_VERIFY_MAX_AGE:-1800}"
PATHS=()

usage() { sed -n '3,50p' "$0" | sed 's/^# \{0,1\}//'; }

die() { printf 'checkpoint: %s\n' "$*" >&2; exit "${2:-1}"; }
note() { printf '%s\n' "$*"; }

while [ $# -gt 0 ]; do
  case "$1" in
    --verify-only)          MODE="verify" ;;
    --dry-run)              DRY_RUN=1 ;;
    --no-verify)            SKIP_VERIFY=1 ;;
    --allow-failing-verify) ALLOW_FAILING_VERIFY=1 ;;
    --allow-unchanged-handoff) ALLOW_UNCHANGED_HANDOFF=1 ;;
    --include-already-staged) INCLUDE_ALREADY_STAGED=1 ;;
    --skip-push)            SKIP_PUSH=1 ;;
    --message|-m)           MESSAGE="${2:-}"; shift ;;
    --verify-cmd)           VERIFY_CMD="${2:-}"; shift ;;
    -h|--help)              usage; exit 0 ;;
    --)                     shift; while [ $# -gt 0 ]; do PATHS+=("$1"); shift; done; break ;;
    -*)                     die "unknown option '$1' (see --help)" 1 ;;
    *)                      PATHS+=("$1") ;;
  esac
  shift
done

# Blanket staging targets are rejected wherever they appear — including after
# `--`. Only explicit file/directory paths may be committed.
for p in "${PATHS[@]}"; do
  case "$p" in
    -A|--all|--ignore-removal|--no-all|.|./|'*'|:*)
      die "refusing blanket staging target '$p'. Pass explicit paths only." 1 ;;
    -*)
      die "refusing path '$p' (looks like an option, not a path)." 1 ;;
  esac
done

# ---------------------------------------------------------------- 0. guards
BRANCH="$(git -C "$REPO_ROOT" rev-parse --abbrev-ref HEAD)"
[ "$BRANCH" = "HEAD" ] && die "detached HEAD — check out a branch before checkpointing" 1

for marker in MERGE_HEAD CHERRY_PICK_HEAD REVERT_HEAD rebase-merge rebase-apply; do
  [ -e "$GIT_DIR/$marker" ] && die "a merge/rebase/cherry-pick is in progress ($marker). Finish or abort it first." 1
done

# ---------------------------------------------------------------- 1. quota
QUOTA_CODE=3
QUOTA_OUT=""
QUOTA_LINE="unavailable (scripts/quota-status.sh not run)"
if [ -x "$SCRIPT_QUOTA" ]; then
  QUOTA_OUT="$("$SCRIPT_QUOTA" 2>&1)"
  QUOTA_CODE=$?
  QUOTA_LINE="$(printf '%s\n' "$QUOTA_OUT" | grep -E '^(quota:|workspace:)' | paste -sd ' | ' -)"
  [ -z "$QUOTA_LINE" ] && QUOTA_LINE="no summary returned"
else
  QUOTA_LINE="scripts/quota-status.sh missing or not executable"
fi
printf 'quota snapshot: %s\n' "$QUOTA_LINE"
[ "$QUOTA_CODE" -eq 10 ] && note "quota WARNING — this checkpoint is time-critical; keep it tight."
[ "$QUOTA_CODE" -eq 20 ] && note "quota EXHAUSTED — do not start any new task after this checkpoint."
if [ "$QUOTA_CODE" -eq 3 ]; then
  note "quota data unavailable -> the usage-limit response is the fallback trigger (documented, not guessed)."
fi
printf '%s\n' "$QUOTA_OUT" > "$CKPT_DIR/last-quota.txt" 2>/dev/null || true

# ---------------------------------------------------------------- 2. verification
VERIF_RESULT="NOT RUN"
VERIF_CMD_USED=""
VERIF_LOG="$CKPT_DIR/last-verification.txt"

run_verify() {
  VERIF_CMD_USED="$1"
  local started log
  started="$(date -Is)"
  log="$CKPT_DIR/last-verify-output.txt"
  note "verification: $VERIF_CMD_USED"
  if (cd "$REPO_ROOT" && eval "$VERIF_CMD_USED") > "$log" 2>&1; then
    VERIF_RESULT="PASS"
  else
    VERIF_RESULT="FAIL"
  fi
  {
    printf 'result=%s\ncommand=%s\nstarted=%s\nfinished=%s\n' \
      "$VERIF_RESULT" "$VERIF_CMD_USED" "$started" "$(date -Is)"
    printf '%s\n' '--- output tail ---'
    tail -n 40 "$log"
  } > "$VERIF_LOG"
  if [ "$VERIF_RESULT" = "FAIL" ]; then
    note "verification: FAIL — last lines of output:"
    tail -n 25 "$log"
  else
    note "verification: PASS"
  fi
}

if [ "$MODE" = "verify" ]; then
  run_verify "$VERIFY_CMD"
  [ "$VERIF_RESULT" = "FAIL" ] && exit 2
  note "recorded in ${VERIF_LOG#$GIT_DIR/}"
  exit 0
fi

if [ "$SKIP_VERIFY" -eq 1 ]; then
  if [ ! -f "$VERIF_LOG" ]; then
    die "--no-verify given but no verification is recorded. Run scripts/checkpoint.sh --verify-only first." 2
  fi
  REC_AGE=$(( $(date +%s) - $(date -r "$VERIF_LOG" +%s) ))
  VERIF_RESULT="$(head -n1 "$VERIF_LOG" | sed -n 's/^result=//p')"
  VERIF_CMD_USED="$(sed -n 's/^command=//p' "$VERIF_LOG" | head -n1)"
  if [ "$REC_AGE" -gt "$VERIFY_MAX_AGE" ]; then
    die "--no-verify: recorded verification is $((REC_AGE / 60)) min old (limit $((VERIFY_MAX_AGE / 60)) min). Re-run it." 2
  fi
  note "verification: $VERIF_RESULT (recorded $((REC_AGE / 60)) min ago: $VERIF_CMD_USED)"
else
  run_verify "$VERIFY_CMD"
fi

if [ "$VERIF_RESULT" = "FAIL" ] && [ "$ALLOW_FAILING_VERIFY" -eq 0 ]; then
  die "verification failed. Fix it, or pass --allow-failing-verify to checkpoint the work-in-progress anyway." 2
fi

# ---------------------------------------------------------------- 3. paths
[ ${#PATHS[@]} -eq 0 ] && die "no paths given. Pass the exact files to commit after '--'. Never checkpoint blindly." 1

PRE_STAGED="$(git -C "$REPO_ROOT" diff --cached --name-only)"
if [ -n "$PRE_STAGED" ] && [ "$INCLUDE_ALREADY_STAGED" -eq 0 ]; then
  # Only pre-staged files OUTSIDE the requested path list are a problem; a
  # previous --dry-run of the same paths is expected and harmless.
  PRE_COVERED="$(git -C "$REPO_ROOT" diff --cached --name-only -- "${PATHS[@]}")"
  PRE_OTHER="$(comm -23 \
    <(printf '%s\n' "$PRE_STAGED" | sort -u) \
    <(printf '%s\n' "$PRE_COVERED" | sort -u))"
  if [ -n "$PRE_OTHER" ]; then
    printf '%s\n' "$PRE_OTHER" >&2
    die "the index already contains staged changes outside the path list above. Unstage them (git restore --staged <path>), add them to the path list, or pass --include-already-staged." 1
  fi
  note "note: some requested paths were already staged (previous dry-run); continuing."
fi

note ""
note "== git status --porcelain (before staging) =="
git -C "$REPO_ROOT" status --porcelain
note ""
note "== complete diff (staged + unstaged, vs HEAD) =="
DIFF_FILE="$CKPT_DIR/last-diff.txt"
git -C "$REPO_ROOT" diff HEAD > "$DIFF_FILE"
cat "$DIFF_FILE"
note "[full diff also saved to ${DIFF_FILE#$GIT_DIR/}]"
note "untracked files are not in the diff; they appear in git status above."

# ---------------------------------------------------------------- 4. stage
note ""
note "== staging explicit paths =="
printf '  %s\n' "${PATHS[@]}"
git -C "$REPO_ROOT" add -- "${PATHS[@]}" || die "git add failed" 1

STAGED="$(git -C "$REPO_ROOT" diff --cached --name-only)"
[ -z "$STAGED" ] && die "nothing is staged after 'git add' — check the paths." 1

if [ -n "$PRE_STAGED" ] && [ "$INCLUDE_ALREADY_STAGED" -eq 1 ]; then
  STAGED="$(printf '%s\n%s\n' "$PRE_STAGED" "$STAGED" | sort -u)"
fi

# ---------------------------------------------------------------- 5. forbidden files
forbidden_reason() {
  local f="$1"
  case "$f" in
    .env.example|*/.env.example) return 1 ;;
  esac
  case "$f" in
    .env|*/.env|*.env)                 echo "environment file"; return 0 ;;
    secrets.json|*/secrets.json)       echo "secrets file"; return 0 ;;
    *appsettings.*.local.json)         echo "local settings override"; return 0 ;;
    *.pem|*.pfx|*.key|*.crt|*.cer)     echo "key/certificate material"; return 0 ;;
    id_rsa*|*/id_rsa*)                 echo "private key"; return 0 ;;
    *.mdf|*.ldf|*.ndf)                 echo "SQL Server data file"; return 0 ;;
    *.db|*.sqlite|*.sqlite3|*.bak)     echo "local database/backup"; return 0 ;;
    *.trx|*.coverage|*.coveragexml)    echo "test result artifact"; return 0 ;;
    *.zip|*.tar|*.gz|*.7z)             echo "archive"; return 0 ;;
    *.log|*.tmp|*.temp|*.swp)          echo "temporary file"; return 0 ;;
    *.dll|*.pdb|*.exe|*.nupkg)         echo "build artifact"; return 0 ;;
    .DS_Store|*/.DS_Store)             echo "OS junk"; return 0 ;;
    */bin/*|*/obj/*|*/TestResults/*|*/publish/*|*/out/*) echo "build output directory"; return 0 ;;
    */.git/*)                          echo "git internals"; return 0 ;;
  esac
  return 1
}

BLOCKED=""
while IFS= read -r f; do
  [ -z "$f" ] && continue
  reason="$(forbidden_reason "$f")"
  if [ -n "$reason" ]; then BLOCKED="$BLOCKED\n  $f  ($reason)"; fi
done <<< "$STAGED"

# Content scan: unambiguous secret material only (no password heuristics, so
# documented dev/test credentials in config are not blocked by accident).
CONTENT_HITS="$(git -C "$REPO_ROOT" diff --cached | grep -nE \
  -e '-----BEGIN [A-Z ]*PRIVATE KEY-----' \
  -e 'gh[pousr]_[A-Za-z0-9]{20,}' \
  -e 'AKIA[0-9A-Z]{16}' \
  | head -n 10)"
if [ -n "$CONTENT_HITS" ]; then
  BLOCKED="$BLOCKED\n  staged diff contains secret-looking content:"
  BLOCKED="$BLOCKED\n$(printf '%s' "$CONTENT_HITS" | sed 's/^/    /')"
fi

if [ -n "$BLOCKED" ]; then
  printf 'checkpoint: refusing to commit forbidden files:\n%b\n' "$BLOCKED" >&2
  printf 'Nothing was committed. The files above are still staged — unstage them with\n' >&2
  printf '  git restore --staged <path>\n' >&2
  printf 'and keep them out of the repository (.gitignore covers most of them).\n' >&2
  exit 4
fi

# ---------------------------------------------------------------- 6. HANDOFF guard
for h in "${HANDOFF_FILES[@]}"; do
  git -C "$REPO_ROOT" ls-files --error-unmatch "$h" >/dev/null 2>&1 \
    || die "HANDOFF file '$h' is not tracked — it cannot be part of a checkpoint." 5
done

UNSTAGED_HANDOFF="$(git -C "$REPO_ROOT" status --porcelain -- "${HANDOFF_FILES[@]}" | grep -E '^.[MD]' || true)"
if [ -n "$UNSTAGED_HANDOFF" ]; then
  printf '%s\n' "$UNSTAGED_HANDOFF" >&2
  die "both HANDOFF files must be fully updated and staged before committing (they still have unstaged changes)." 5
fi

HANDOFF_CHANGED=0
for h in "${HANDOFF_FILES[@]}"; do
  git -C "$REPO_ROOT" diff --cached --quiet -- "$h" || HANDOFF_CHANGED=1
done

if [ "$HANDOFF_CHANGED" -eq 0 ]; then
  if [ "$ALLOW_UNCHANGED_HANDOFF" -eq 0 ]; then
    printf 'checkpoint: neither HANDOFF.md nor docs/HANDOFF.md changed in this checkpoint.\n' >&2
    printf 'Update BOTH with: completed work, verification results (%s), current state,\n' "$VERIF_RESULT" >&2
    printf 'and exact next steps — then re-run. Use --allow-unchanged-handoff only if the\n' >&2
    printf 'HANDOFF content is already current.\n' >&2
    exit 5
  fi
  note "HANDOFF guard: skipped (--allow-unchanged-handoff)"
else
  # `git diff --quiet` exits 1 when there IS a difference, so a file that is
  # NOT staged makes this succeed and must fail the guard.
  for h in "${HANDOFF_FILES[@]}"; do
    if git -C "$REPO_ROOT" diff --cached --quiet -- "$h"; then
      die "HANDOFF file '$h' is not staged. Update both HANDOFF files and stage them." 5
    fi
  done
  note "HANDOFF guard: both HANDOFF.md and docs/HANDOFF.md are staged"
fi

# ---------------------------------------------------------------- 7. pre-push check
if [ "$SKIP_PUSH" -eq 0 ]; then
  if ! git -C "$REPO_ROOT" fetch --quiet origin "refs/heads/$BRANCH" 2>/dev/null; then
    note "warning: could not fetch origin (offline?). Push will be attempted and verified afterwards."
  else
    BEHIND="$(git -C "$REPO_ROOT" rev-list --count "HEAD..FETCH_HEAD" 2>/dev/null || echo 0)"
    if [ "${BEHIND:-0}" -gt 0 ]; then
      die "origin/$BRANCH has $BEHIND commit(s) that this branch does not. Pull/rebase with owner approval before pushing — never force-push." 7
    fi
  fi
fi

note ""
note "== files to be committed =="
printf '%s\n' "$STAGED"

if [ "$DRY_RUN" -eq 1 ]; then
  note ""
  note "DRY RUN — nothing was committed or pushed. The paths above are staged in the"
  note "index so the guards could inspect them. Re-run without --dry-run to commit."
  exit 0
fi

# ---------------------------------------------------------------- 8. commit
[ -n "$MESSAGE" ] || die "a commit message is required (--message). Put the requirement ID in the title (NFR-051)." 1

note ""
note "== committing =="
git -C "$REPO_ROOT" commit -m "$MESSAGE" || die "commit failed" 1
LOCAL_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
note "local commit: $LOCAL_SHA"
note "$(git -C "$REPO_ROOT" log -1 --pretty=format:'             %s')"

# ---------------------------------------------------------------- 9. push + remote proof
PUSH_STATE="not attempted"
REMOTE_SHA=""

if [ "$SKIP_PUSH" -eq 1 ]; then
  PUSH_STATE="SKIPPED (--skip-push) — the commit exists locally only"
else
  note ""
  note "== pushing to origin/$BRANCH (no force) =="
  if git -C "$REPO_ROOT" push origin "HEAD:refs/heads/$BRANCH"; then
    REMOTE_SHA="$(git -C "$REPO_ROOT" ls-remote --refs origin "refs/heads/$BRANCH" | awk '{print $1}')"
    if [ "$REMOTE_SHA" = "$LOCAL_SHA" ]; then
      PUSH_STATE="VERIFIED — remote SHA matches local commit"
    else
      PUSH_STATE="FAILED — remote is '$REMOTE_SHA', local is '$LOCAL_SHA'"
    fi
  else
    PUSH_STATE="FAILED — 'git push' reported an error"
  fi
fi

# ---------------------------------------------------------------- 10. report
REMAINING="$(git -C "$REPO_ROOT" status --porcelain)"
[ -z "$REMAINING" ] && REMAINING="(clean working tree)"

REPORT="$CKPT_DIR/last-report.txt"
{
  echo "=== PRE-QUOTA CHECKPOINT REPORT ($(date -Is)) ==="
  echo "branch:        origin/$BRANCH"
  echo "quota:         $QUOTA_LINE"
  echo "verification:  $VERIF_RESULT ($VERIF_CMD_USED)"
  echo "commit:        $LOCAL_SHA"
  echo "push:          $PUSH_STATE"
  echo "remote:        ${REMOTE_SHA:-n/a}"
  echo ""
  echo "committed files:"
  printf '%s\n' "$STAGED" | sed 's/^/  /'
  echo ""
  echo "still dirty:"
  printf '%s\n' "$REMAINING"
} > "$REPORT"
cat "$REPORT"
note ""

case "$PUSH_STATE" in
SKIPPED*)
  cat <<EOF
NEXT STEPS (checkpoint incomplete — local only):
  1. Push when ready:  git push origin HEAD:refs/heads/$BRANCH
     then prove it:    git ls-remote --refs origin refs/heads/$BRANCH
                       (must equal $LOCAL_SHA)
  2. Re-read the HANDOFF files to confirm they describe this commit.
  3. Do not start new tasks if the quota warning above is active.
EOF
  exit 0 ;;
esac

# NOTE: use `case`, not [ "$x" != VERIFIED* ] — bash's test(1) compares the
# right operand literally, so the glob never matches (regression: self-test).
case "$PUSH_STATE" in
VERIFIED*) ;;
*)
  cat <<EOF
CHECKPOINT FAILED AT THE LAST STEP — the work is committed locally but the
remote does NOT have it (or it was not verified).
  local commit:   $LOCAL_SHA
  push state:     $PUSH_STATE
  retry:          git push origin HEAD:refs/heads/$BRANCH
  then verify:    git ls-remote --refs origin refs/heads/$BRANCH
  If the push is rejected because origin moved, pull/rebase with owner approval.
  Never force-push.
EOF
  exit 6 ;;
esac

cat <<EOF
CHECKPOINT VERIFIED — safe to stop here.
  local SHA == remote SHA == $LOCAL_SHA on origin/$BRANCH
  verification: $VERIF_RESULT
  quota:        $QUOTA_LINE

Resume later (or after the quota resets):
  1. git fetch origin && git status        # confirm you are on the checkpoint
  2. Read HANDOFF.md (or docs/HANDOFF.md) — it holds the state and next steps
  3. Run scripts/quota-status.sh before starting anything substantial
  4. Continue from the "next steps" recorded in the HANDOFF files
EOF
exit 0
