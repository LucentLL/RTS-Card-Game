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

# THE BUILD'S OWN NAME. Pages serves every Build file with its own max-age=600 and the edges
# expire independently, so for a window after each deploy a returning player can be handed a NEW
# .wasm beside an OLD .data. Unity indexes that data blob by byte offset, so the pair is not
# "out of date", it is garbage - and it presents as a loading bar that never finishes.
#
# index.html reads this before it loads anything and stamps it onto all four URLs, so a build's
# URLs have never been requested before and nothing stale can answer them. Committed with the
# build it names.
STAMP="$(date -u +%Y%m%d%H%M%S)-$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || echo local)"
printf '%s' "$STAMP" > "$DST/Build/version.txt"

echo "staged -> play/ ($(du -sh "$DST/Build" | cut -f1)) version=$STAMP"
