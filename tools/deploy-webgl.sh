#!/usr/bin/env bash
# Build the WebGL player headlessly and stage it into play/ for GitHub Pages.
#
# NO -quit, for the same reason run-unity-tests.sh does not use it: with -quit the editor
# quits as soon as its main loop goes idle, and BuildPipeline.BuildPlayer hands the heavy half
# of the work - il2cpp, the wasm link, brotli - to the Bee backend, which the editor then pumps.
# Quit during that and the task is cancelled mid-flight: "Tundra build interrupted" and a
# TaskCanceledException, at a different step each run. It only started biting when debug symbols
# made the link slow enough to cross the line. WebGLBuild.Build exits explicitly instead.
# Usage: bash tools/deploy-webgl.sh          (build + stage; commit and push separately)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
LOG="$ROOT/unity/Logs/webgl-build.log"

mkdir -p "$ROOT/unity/Logs"

"$UNITY" \
  -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -executeMethod WebGLBuild.Build \
  -logFile "$LOG" \
  -silent-crashes -accept-apiupdate
code=$?
echo "unity exit=$code"
grep -o '\[build\] result=.*' "$LOG" | tail -1
if [ $code -ne 0 ]; then
  grep -i "error" "$LOG" | tail -20
  exit $code
fi

SRC="$ROOT/unity/Build/WebGL"
DST="$ROOT/play"
[ -d "$SRC/Build" ] || { echo "no build output at $SRC"; exit 1; }

rm -rf "$DST/Build"
cp -r "$SRC/Build" "$DST/Build"
cp "$SRC/index.html" "$DST/index.html"
echo "staged -> play/ ($(du -sh "$DST/Build" | cut -f1))"
