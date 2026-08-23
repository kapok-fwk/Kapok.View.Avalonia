#!/usr/bin/env bash
# Runs every KAPOK_HEADLESS_SCREENSHOT_* scenario ToDoAvaloniaApp's headless harness supports
# (see src/ToDoAvaloniaApp/Program.cs and App.cs) as a real cross-platform smoke test - the harness
# uses Avalonia.Headless with Skia *software* rendering (UseHeadlessDrawing=false), which needs no
# display, so it runs the same way in CI as it does on a dev machine.
#
# Shared between the "Headless verification (Linux)" CI job and manual local runs (macOS or
# Linux) - kept as its own script rather than inline YAML specifically so it can be run locally,
# once with a known-good backend, to check the scenario list and each command's env vars are
# correct independently of whatever is Linux-specific about the CI runner itself.
#
# Usage: run-headless-verification.sh <output-dir>
# Exit code is non-zero if any scenario's `dotnet run` exited non-zero or timed out.
set -uo pipefail

OUT_DIR="${1:?usage: run-headless-verification.sh <output-dir>}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$REPO_ROOT/src/ToDoAvaloniaApp/ToDoAvaloniaApp.csproj"
TIMEOUT_BIN="timeout"
# macOS has no GNU `timeout` by default (coreutils installs it as `gtimeout`); fall back to
# running unbounded there rather than failing the whole script - CI (ubuntu-latest) always has
# GNU coreutils' `timeout`, so this only matters for local runs.
if ! command -v timeout >/dev/null 2>&1; then
  if command -v gtimeout >/dev/null 2>&1; then
    TIMEOUT_BIN="gtimeout"
  else
    TIMEOUT_BIN=""
  fi
fi

mkdir -p "$OUT_DIR"
FAILED_FILE="$OUT_DIR/FAILED.txt"
rm -f "$FAILED_FILE"

run_scenario() {
  local name="$1"
  shift
  local out="$OUT_DIR/$name"
  echo "::group::Scenario: $name"
  echo "env: $* KAPOK_HEADLESS_SCREENSHOT=${out}.png"

  if [ -n "$TIMEOUT_BIN" ]; then
    "$TIMEOUT_BIN" 90s env "$@" KAPOK_HEADLESS_SCREENSHOT="${out}.png" \
      dotnet run --no-build -c Debug --project "$PROJECT" \
      > "${out}.log" 2>&1
  else
    env "$@" KAPOK_HEADLESS_SCREENSHOT="${out}.png" \
      dotnet run --no-build -c Debug --project "$PROJECT" \
      > "${out}.log" 2>&1
  fi
  local status=$?

  # Headless runs sometimes don't exit on their own (see the porting plan's handoff notes) -
  # clean up any stray process from a timed-out or otherwise-stuck run before moving to the next
  # scenario, so it can't hold onto state (e.g. the Sqlite in-memory connection) across processes.
  pkill -f ToDoAvaloniaApp >/dev/null 2>&1 || true

  echo "exit code: $status"
  cat "${out}.log"
  echo "::endgroup::"

  if [ "$status" -ne 0 ]; then
    echo "$name (exit $status)" >> "$FAILED_FILE"
  fi
}

# Matches the scenario list from the porting plan's Phase 8 handoff, plus OPEN_LOOKUP - the
# still-open LookupComboBox dropdown gap (itemsCount=1 columns=0 rows=0 in every macOS run so
# far) - included here because a differing result on Linux would itself be the finding that gap
# needs: either it reproduces identically (a real headless-only limitation, not OS-specific) or it
# doesn't (pointing at something macOS/Avalonia.Native-specific instead).
run_scenario mainpage
run_scenario testpage KAPOK_HEADLESS_SCREENSHOT_PAGE=TestPage
run_scenario tasklists-empty KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskLists
run_scenario tasklists-seeded KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskLists KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_DUMP_COLUMNS=1
run_scenario tasks-seeded KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_SEED=1
run_scenario taskcategories KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskCategories KAPOK_HEADLESS_SCREENSHOT_SEED=1
run_scenario selection KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskLists KAPOK_HEADLESS_SCREENSHOT_SELECTION=1
run_scenario filter KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_FILTER=1
run_scenario paste KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_PASTE=1
run_scenario row-drag KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskCategories KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_ROW_DRAG=1
run_scenario hierarchy-nav KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskCategories KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_HIERARCHY_NAV=1
run_scenario nav KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_NAV=1
run_scenario list-toolbar KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_LIST_TOOLBAR=1
run_scenario drilldown KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskLists KAPOK_HEADLESS_SCREENSHOT_DRILLDOWN=1
run_scenario lookup-edit KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_LOOKUP_EDIT=1
run_scenario row-style KAPOK_HEADLESS_SCREENSHOT_PAGE=Tasks KAPOK_HEADLESS_SCREENSHOT_ROW_STYLE=1
run_scenario drop-file KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskCard KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_DROP_FILE=1
run_scenario open-lookup KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskCard KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_OPEN_LOOKUP=1
run_scenario report KAPOK_HEADLESS_SCREENSHOT_PAGE=TaskLists KAPOK_HEADLESS_SCREENSHOT_SEED=1 KAPOK_HEADLESS_SCREENSHOT_REPORT=1 KAPOK_HEADLESS_SCREENSHOT_REPORT_SAVE=1

if [ -s "$FAILED_FILE" ]; then
  echo "Scenario(s) exited non-zero or timed out:"
  cat "$FAILED_FILE"
  exit 1
fi

echo "All headless verification scenarios completed successfully."
