#!/usr/bin/env bash
#
# checkpoint-selftest.sh — regression tests for scripts/checkpoint.sh guards.
#
# Runs entirely inside throwaway repositories under $TMPDIR; it never touches
# the SupplyForge working tree, its index, or any remote. Run it after changing
# checkpoint.sh:
#
#   scripts/checkpoint-selftest.sh
#
# Exit 0 when every case passes, 1 otherwise.
#
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$ROOT/scripts/checkpoint.sh"
WORK="$(mktemp -d)"
OUT="$WORK/out.log"
PASS=0
FAIL=0

cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

[ -f "$SRC" ] || { echo "selftest: $SRC not found" >&2; exit 1; }

# Fresh repo: both HANDOFF files committed, then modified in the worktree
# (so the happy paths have something real to update and stage).
new_repo() {
  rm -rf "$WORK/repo" "$WORK/remote.git"
  mkdir -p "$WORK/repo/scripts" "$WORK/repo/docs"
  git init -q "$WORK/repo"
  git -C "$WORK/repo" config user.email selftest@example.com
  git -C "$WORK/repo" config user.name selftest
  cp "$SRC" "$WORK/repo/scripts/checkpoint.sh"
  chmod +x "$WORK/repo/scripts/checkpoint.sh"
  printf 'v1\n' > "$WORK/repo/HANDOFF.md"
  printf 'v1\n' > "$WORK/repo/docs/HANDOFF.md"
  git -C "$WORK/repo" add HANDOFF.md docs/HANDOFF.md
  git -C "$WORK/repo" commit -qm init
  printf 'v2\n' >> "$WORK/repo/HANDOFF.md"
  printf 'v2\n' >> "$WORK/repo/docs/HANDOFF.md"
}

with_origin() { # attach a local bare repo as origin
  git init -q --bare "$WORK/remote.git"
  git -C "$WORK/repo" remote add origin "$WORK/remote.git"
}

run() { # clear the index, run checkpoint.sh with the given args, print its exit code
  ( cd "$WORK/repo" && git reset -q && ./scripts/checkpoint.sh "$@" ) > "$OUT" 2>&1
  echo $?
}

expect() { # label expected_code actual_code
  if [ "$2" = "$3" ]; then
    PASS=$((PASS + 1)); printf 'ok    %-36s exit %s\n' "$1" "$3"
  else
    FAIL=$((FAIL + 1)); printf 'FAIL  %-36s expected %s, got %s\n' "$1" "$2" "$3"
    sed 's/^/        | /' "$OUT"
  fi
}

expect_grep() { # label fixed_string  (searched in $OUT)
  if grep -qF "$2" "$OUT"; then
    PASS=$((PASS + 1)); printf 'ok    %-36s contains "%s"\n' "$1" "$2"
  else
    FAIL=$((FAIL + 1)); printf 'FAIL  %-36s missing "%s"\n' "$1" "$2"
    sed 's/^/        | /' "$OUT"
  fi
}

echo "== checkpoint.sh guard self-test =="

# 1. environment file refused (.env.example would be allowed)
new_repo
printf 'SECRET=dummy\n' > "$WORK/repo/.env"
expect "env file refused" 4 \
  "$(run --dry-run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md .env)"

# 2. blanket staging targets refused, including after '--'
expect "blanket '.' refused" 1 \
  "$(run --dry-run --verify-cmd true -m t -- .)"
expect "blanket '*' refused" 1 \
  "$(run --dry-run --verify-cmd true -m t -- '*')"

# 3. --no-verify without a recorded verification
rm -f "$WORK/repo/.git/checkpoint/last-verification.txt"
expect "--no-verify with no record" 2 \
  "$(run --dry-run --no-verify -m t -- HANDOFF.md docs/HANDOFF.md)"

# 4. --no-verify with a stale record
mkdir -p "$WORK/repo/.git/checkpoint"
printf 'result=PASS\ncommand=true\nstarted=x\nfinished=x\n' \
  > "$WORK/repo/.git/checkpoint/last-verification.txt"
touch -d '2 hours ago' "$WORK/repo/.git/checkpoint/last-verification.txt"
expect "--no-verify with stale record" 2 \
  "$(run --dry-run --no-verify -m t -- HANDOFF.md docs/HANDOFF.md)"

# 5. private key material in the staged diff, on a path that no path rule
#    matches, so the content scan is what has to catch it. The marker is
#    assembled at runtime: this source file must not contain it, or committing
#    the test would trip the very guard it tests.
printf -- '%s%s\n%s\n' '-----BEGIN RSA ' 'PRIVATE KEY-----' 'abc' > "$WORK/repo/deploy.conf"
expect "private key content refused" 4 \
  "$(run --dry-run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md deploy.conf)"

# 6. only one of the two HANDOFF files updated
git -C "$WORK/repo" checkout -q HEAD -- HANDOFF.md
expect "one HANDOFF file only" 5 \
  "$(run --dry-run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md)"
printf 'v2\n' >> "$WORK/repo/HANDOFF.md"

# 7. neither HANDOFF file updated (and the documented escape hatch)
new_repo
git -C "$WORK/repo" checkout -q HEAD -- HANDOFF.md docs/HANDOFF.md
printf 'note\n' > "$WORK/repo/notes.txt"
expect "unchanged HANDOFF refused" 5 \
  "$(run --dry-run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md notes.txt)"
expect "escape hatch allows it" 0 \
  "$(run --dry-run --allow-unchanged-handoff --verify-cmd true -m t \
      -- HANDOFF.md docs/HANDOFF.md notes.txt)"

# 8. missing commit message (HANDOFFs updated, so the guard reaches the message check)
new_repo
expect "missing commit message" 1 \
  "$(run --verify-cmd true -- HANDOFF.md docs/HANDOFF.md)"

# 9. happy dry-run stops before committing
new_repo
expect "happy dry-run" 0 \
  "$(run --dry-run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md)"
expect_grep "dry-run stops before commit" "DRY RUN"

# 10. full run with --skip-push (the SKIPPED branch)
new_repo
with_origin
expect "--skip-push commits locally" 0 \
  "$(run --skip-push --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md)"
expect_grep "--skip-push reports pending push" "checkpoint incomplete — local only"

# 11. full run against a real remote: local SHA must equal remote SHA
new_repo
with_origin
expect "full run with push" 0 \
  "$(run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md)"
expect_grep "push only claimed when verified" "CHECKPOINT VERIFIED"
LOCAL_SHA="$(git -C "$WORK/repo" rev-parse HEAD)"
REMOTE_SHA="$(git -C "$WORK/remote.git" rev-parse HEAD)"
if [ -n "$LOCAL_SHA" ] && [ "$LOCAL_SHA" = "$REMOTE_SHA" ]; then
  PASS=$((PASS + 1)); printf 'ok    %-36s %s\n' "remote SHA equals local" "$LOCAL_SHA"
else
  FAIL=$((FAIL + 1)); printf 'FAIL  %-36s local %s != remote %s\n' \
    "remote SHA equals local" "$LOCAL_SHA" "$REMOTE_SHA"
fi

# 12. a failed push must be reported as failure, never as verified
new_repo
git -C "$WORK/repo" remote add origin "$WORK/definitely-not-a-repo.git"
expect "push failure reported as exit 6" 6 \
  "$(run --verify-cmd true -m t -- HANDOFF.md docs/HANDOFF.md)"
expect_grep "push failure not claimed verified" "CHECKPOINT FAILED AT THE LAST STEP"

echo
echo "self-test: $PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ] || exit 1
exit 0
