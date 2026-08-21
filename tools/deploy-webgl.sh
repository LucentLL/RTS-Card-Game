#!/usr/bin/env bash
# Build the WebGL player headlessly and stage it into play/ for GitHub Pages.
# Usage: bash tools/deploy-webgl.sh          (build + stage; commit and push separately)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
LOG="$ROOT/unity/Logs/webgl-build.log"

mkdir -p "$ROOT/unity/Logs"

"$UNITY" \
  -batchmode -nographics -quit \
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
