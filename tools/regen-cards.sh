#!/usr/bin/env bash
# Full card-data regeneration, one command (design 03 s5.7):
#   JS registry -> cards.json -> CardDefinition assets + CardDatabase.
# Run after ANY card edit in src/js/, then commit all three layers together.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"

node "$ROOT/tools/export_cards.mjs"
node "$ROOT/tools/setup-unity-links.mjs"

"$UNITY" -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -executeMethod SpawnRowDuel.EditorPipeline.CardImportCli.ImportAndExit \
  -logFile "$ROOT/unity/Logs/card-import.log" \
  -silent-crashes -accept-apiupdate
echo "import exit=$?"

git -C "$ROOT" status --short docs/unity/spec/cards.json unity/Assets/Game/Data | head -20
