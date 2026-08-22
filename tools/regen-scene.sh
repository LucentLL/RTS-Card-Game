#!/usr/bin/env bash
# Rebuild the Battle scene from SceneBootstrap. The scene is GENERATED, not hand-edited: a
# component added by hand in the editor is lost the next time this runs.
#
#   bash tools/regen-scene.sh
#
# Do NOT run while the test gate or a WebGL build is running - Unity locks the project folder.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
LOG="$ROOT/unity/Logs/regen-scene.log"

mkdir -p "$ROOT/unity/Logs"

"$UNITY" -batchmode -nographics -quit \
  -projectPath "$ROOT/unity" \
  -executeMethod SceneBootstrap.Build \
  -logFile "$LOG" \
  -silent-crashes -accept-apiupdate
code=$?

echo "unity exit=$code"
grep -E "\[scene\]|error CS|Shader error|shader not found" "$LOG" | head -20 || true
exit $code
