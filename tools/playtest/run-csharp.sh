#!/usr/bin/env bash
# AI-vs-AI self-play over the SHIPPED C# rules (see unity/Assets/Editor/PlaytestCli.cs).
#   bash tools/playtest/run-csharp.sh                 # 8x8 mono pairings x 4 seeds, 300-turn cap
#   SRD_PLAYTEST_SEEDS=8 bash tools/playtest/run-csharp.sh
#   node tools/playtest/analyze-csharp.mjs            # then read it
# NO -quit, for the reason run-unity-tests.sh gives.
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
UNITY="C:/Program Files/Unity/Hub/Editor/6000.5.5f1/Editor/Unity.exe"
LOG="$ROOT/unity/Logs/playtest.log"

export SRD_PLAYTEST_OUT="${SRD_PLAYTEST_OUT:-$ROOT/tools/playtest/out/csharp-selfplay.json}"

"$UNITY" -batchmode -nographics \
  -projectPath "$ROOT/unity" \
  -executeMethod SpawnRowDuel.EditorPipeline.PlaytestCli.SelfPlayAndExit \
  -logFile "$LOG" \
  -silent-crashes -accept-apiupdate
code=$?
echo "unity exit=$code"
grep -o '\[playtest\] .*' "$LOG" | tail -1
if [ $code -ne 0 ]; then grep -iE "error|exception" "$LOG" | grep -v "^Refresh" | tail -20; fi
exit $code
