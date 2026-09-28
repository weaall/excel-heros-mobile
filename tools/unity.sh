#!/bin/bash
# Run one editor method in batch mode WITH graphics (preview renders come out grey with -nographics).
#   bash tools/unity.sh ExcelHeroes.EditorTools.SdBasePreview.Run [log name]
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WROOT="$(cygpath -w "$ROOT" 2>/dev/null || echo "$ROOT")"
U="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.0.82f1/Editor/Unity.exe}"
LOG="$ROOT/tools/out/${2:-unity}.log"; mkdir -p "$ROOT/tools/out"
"$U" -batchmode -quit -projectPath "$WROOT" -executeMethod "$1" -logFile "$LOG"
rc=$?
if grep -q "error CS" "$LOG"; then echo "COMPILE ERRORS:"; grep "error CS" "$LOG" | sort -u | head -20; exit 1; fi
grep -E "Exception" "$LOG" | sort | uniq -c | head -5
echo "rc=$rc log=$LOG"
