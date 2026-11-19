#!/usr/bin/env bash
#
# quota-status.sh — read OpenCode usage limits from the official endpoint.
#
# Official source (no guessing, no third-party scraping):
#   GET /api/experimental/usage/limits      (OpenCode server HttpApi, operationId
#                                            "experimental.usage.limits")
#   Summary: "Return the OpenCode Go allowance and Console budgets of the
#             OpenCode Console workspace the location is signed in to, or null
#             when it isn't signed in."
#
# The response carries:
#   .data.go.{rolling,weekly,monthly}.{percent,limited,resetsAt}   allowance meters
#   .data.budgets[]  {limit,spent,...}                              Console budgets
#   .data.workspace.{id,name,usageURL}
#
# When the workspace is not signed in, or the server returns no meters, this
# script reports UNKNOWN and prints no number. It does not guess. The fallback
# trigger in that case is a usage-limit response from the model provider
# (HTTP 429 / a "usage limit" error) — see docs/pre-quota-checkpoint.md.
#
# Exit codes:
#   0   quota data available, below the warning threshold
#   10  quota data available, at or above $QUOTA_WARN_PERCENT -> checkpoint now
#   20  a meter is already limited -> quota exhausted, checkpoint immediately
#   3   quota data unavailable -> use the usage-limit response as the trigger
#   1   script error (missing dependency, unreadable response)
#
# Usage:
#   scripts/quota-status.sh            human summary
#   scripts/quota-status.sh --json     raw endpoint response
#   QUOTA_WARN_PERCENT=70 scripts/quota-status.sh
#
set -uo pipefail

WARN_PERCENT="${QUOTA_WARN_PERCENT:-80}"
ENDPOINT="/api/experimental/usage/limits"

fail() { printf 'quota-status: %s\n' "$*" >&2; exit 1; }

unknown() {
  printf 'quota: UNKNOWN — %s\n' "$1"
  printf '        No number is reported because none is available. Do not guess.\n'
  printf 'fallback trigger: a usage-limit response from the provider (HTTP 429 /\n'
  printf '                  "usage limit" error). On that response: stop new work\n'
  printf '                  and run scripts/checkpoint.sh.\n'
  exit 3
}

command -v opencode >/dev/null 2>&1 || fail "'opencode' not found on PATH"
command -v jq      >/dev/null 2>&1 || fail "'jq' not found on PATH"

RAW="$(opencode api GET "$ENDPOINT" 2>/dev/null || true)"

if [ -z "$RAW" ]; then
  unknown "the OpenCode server did not answer (start it with 'opencode service', or run inside an OpenCode session)"
fi

if ! printf '%s' "$RAW" | jq -e . >/dev/null 2>&1; then
  unknown "the endpoint returned a non-JSON response"
fi

if [ "${1:-}" = "--json" ]; then
  printf '%s\n' "$RAW" | jq .
  exit 0
fi

if [ "$(printf '%s' "$RAW" | jq -r 'if .data == null then "yes" else "no" end')" = "yes" ]; then
  unknown "this location is not signed in to an OpenCode Console workspace (data is null)"
fi

WS_NAME="$(printf '%s' "$RAW" | jq -r '.data.workspace.name // "unnamed workspace"')"
WS_URL="$(printf '%s' "$RAW" | jq -r '.data.workspace.usageURL // ""')"
printf 'workspace: %s\n' "$WS_NAME"
if [ -n "$WS_URL" ] && [ "$WS_URL" != "null" ]; then
  printf 'console:   %s\n' "$WS_URL"
fi

fmt_reset() {
  local ms="${1:-}"
  case "$ms" in ''|null) printf 'n/a'; return ;; esac
  if date -d "@$((ms / 1000))" '+%Y-%m-%d %H:%M %Z' 2>/dev/null; then return; fi
  printf '%s' "$ms"
}

HAS_GO="$(printf '%s' "$RAW" | jq -r 'if .data.go == null then "no" else "yes" end')"
BUDGET_COUNT="$(printf '%s' "$RAW" | jq -r '(.data.budgets // []) | length')"

if [ "$HAS_GO" = "yes" ]; then
  printf '\nOpenCode Go allowance:\n'
  printf '  %-8s  %6s  %-7s  %s\n' "meter" "used" "limited" "resets"
  printf '%s' "$RAW" | jq -r '
    .data.go as $g
    | ["rolling", $g.rolling.percent, $g.rolling.limited, $g.rolling.resetsAt],
      ["weekly",  $g.weekly.percent,  $g.weekly.limited,  $g.weekly.resetsAt],
      ["monthly", $g.monthly.percent, $g.monthly.limited, $g.monthly.resetsAt]
    | @tsv' | while IFS=$'\t' read -r meter pct limited resets; do
      printf '  %-8s  %5s%%  %-7s  %s\n' "$meter" "$pct" "$limited" "$(fmt_reset "$resets")"
    done
fi

if [ "$BUDGET_COUNT" != "0" ]; then
  printf '\nConsole budgets:\n'
  printf '%s' "$RAW" | jq -r '
    .data.budgets[]
    | "  \(.id): spent $\(.spent) of $\(.limit) (\(if .limit == 0 then 0 else ((.spent / .limit) * 100 | round) end)%) resets \(if .resetsAt == null then "n/a" else (.resetsAt | tostring) end)"'
fi

if [ "$HAS_GO" = "no" ] && [ "$BUDGET_COUNT" = "0" ]; then
  printf '\n'
  unknown "the workspace returned neither Go allowance meters nor budgets"
fi

if [ "$HAS_GO" = "yes" ]; then
  MAX_PCT="$(printf '%s' "$RAW" | jq '[.data.go.rolling.percent, .data.go.weekly.percent, .data.go.monthly.percent] | max')"
  ANY_LIMITED="$(printf '%s' "$RAW" | jq '([.data.go.rolling.limited, .data.go.weekly.limited, .data.go.monthly.limited] | any)')"

  if [ "$ANY_LIMITED" = "true" ]; then
    printf '\nquota: EXHAUSTED — a meter is already limited. Stop new tasks and checkpoint now.\n'
    exit 20
  fi

  if awk "BEGIN { exit !($MAX_PCT >= $WARN_PERCENT) }"; then
    printf '\nquota: WARNING — highest meter at %s%% (threshold %s%%). Do not start new tasks;\n' "$MAX_PCT" "$WARN_PERCENT"
    printf '       finish the current operation and run scripts/checkpoint.sh.\n'
    exit 10
  fi

  printf '\nquota: OK — highest meter at %s%% (threshold %s%%).\n' "$MAX_PCT" "$WARN_PERCENT"
  exit 0
fi

printf '\nquota: OK — no allowance meters returned; only Console budgets are configured.\n'
exit 0
