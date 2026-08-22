#!/usr/bin/env bash
# Screenshot the real battle screen from play mode, headlessly.
#
#   tools/screenshot.sh [outdir]
#
# Writes battle-open.png and battle-mid.png. No -nographics: capturing a composited frame needs a
# graphics device, and the usual batchmode flags would produce nothing at all.
#
# Do NOT run while a WebGL build or the EditMode gate is running - Unity locks the project folder
# and the second process exits having done nothing.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
OUT="${1:-$ROOT/unity/Build/Probe}"
RESULTS="$ROOT/unity/Build/TestResults-PlayMode.xml"

mkdir -p "$OUT"

SRD_SHOT_DIR="$OUT" "$UNITY" \
  -runTests -batchmode \
  -projectPath "$ROOT/unity" \
  -testPlatform PlayMode \
  -testFilter "SpawnRowDuel.PlayTests.BattleScreenshotTests" \
  -testResults "$RESULTS" \
  -logFile "$ROOT/unity/Logs/screenshot.log" \
  -silent-crashes -accept-apiupdate
code=$?

echo "unity exit=$code"
grep -E "shot wrote|error CS|Exception" "$ROOT/unity/Logs/screenshot.log" | head -10 || true
ls -la "$OUT"/*.png 2>/dev/null | awk '{print $5, $9}'
exit $code
