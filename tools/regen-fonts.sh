#!/usr/bin/env bash
# Rebuild the font assets from the OFL sources in unity/Assets/Game/Fonts/Source.
#
#   node tools/export_glyphs.mjs   -> docs/unity/spec/glyphs.txt (the required set)
#   FontPipeline                   -> SRD-Display-*/SRD-Body-* (dynamic) + SRD-Symbols/Emoji/CJK
#                                     (static, baked from the list, source font stripped)
#
# Exit 2 means a required glyph has no home in the chain - that is the tofu gate, and it is a
# failure, not a warning.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"

node "$ROOT/tools/export_glyphs.mjs"

"$UNITY" -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -executeMethod SpawnRowDuel.EditorPipeline.FontPipelineCli.BuildAndExit \
  -logFile "$ROOT/unity/Logs/font-pipeline.log" \
  -silent-crashes -accept-apiupdate
code=$?

echo "font pipeline exit=$code"
grep -E "font pipeline:|MISSING|has no glyph" "$ROOT/unity/Logs/font-pipeline.log" | head -20 || true
exit $code
