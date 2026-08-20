#!/usr/bin/env bash
# EditMode test gate for the Unity port. Usage:
#   tools/run-unity-tests.sh                 # whole suite
#   tools/run-unity-tests.sh CatalogTests    # -testFilter value
#
# Exit codes (Unity Test Framework): 0 = pass, 2 = test failures, 3 = run could not start.
# NEVER add -quit: it terminates the editor before the run completes (design 03 s8.2).
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
RESULTS="$ROOT/unity/Build/TestResults-EditMode.xml"
LOG="$ROOT/unity/Logs/test-run.log"

mkdir -p "$ROOT/unity/Build"

FILTER_ARGS=()
if [ "${1:-}" != "" ]; then
  FILTER_ARGS=(-testFilter "$1")
fi

"$UNITY" \
  -runTests -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -testPlatform EditMode \
  -testResults "$RESULTS" \
  -logFile "$LOG" \
  -silent-crashes -accept-apiupdate \
  "${FILTER_ARGS[@]}"
code=$?

echo "unity exit=$code"
if [ -f "$RESULTS" ]; then
  # one-line summary from the NUnit3 XML
  grep -o '<test-run[^>]*' "$RESULTS" | head -1 \
    | sed 's/.*total="\([0-9]*\)".*passed="\([0-9]*\)".*failed="\([0-9]*\)".*/total=\1 passed=\2 failed=\3/'
fi
exit $code
