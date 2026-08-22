#!/usr/bin/env bash
# Render a view surface to a PNG so it can be LOOKED at, without a WebGL build and a deploy.
#
#   tools/view-probe.sh [output.png]
#
# NOTE: no -nographics. UI Toolkit needs a real graphics device to repaint into a RenderTexture,
# and the usual batchmode flags would silently produce a black image.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
OUT="${1:-$ROOT/unity/Build/Probe/cards.png}"

mkdir -p "$(dirname "$OUT")"
rm -f "$OUT"

SRD_PROBE_OUT="$OUT" "$UNITY" -batchmode \
  -projectPath "$ROOT/unity" \
  -executeMethod SpawnRowDuel.EditorPipeline.ViewProbe.CaptureAndExit \
  -logFile "$ROOT/unity/Logs/view-probe.log" \
  -silent-crashes -accept-apiupdate
code=$?

echo "probe exit=$code"
grep -E "probe wrote|error CS|Exception|MissingReference" "$ROOT/unity/Logs/view-probe.log" | head -10 || true
[ -f "$OUT" ] && echo "wrote $OUT ($(stat -c%s "$OUT") bytes)" || echo "NO IMAGE WRITTEN"
exit $code
