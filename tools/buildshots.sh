#!/bin/bash
# Build the Windows player headlessly, run it once, capture every screen into tools/out/shots.
#   bash tools/buildshots.sh            # 32 screens
#   BURST=1 bash tools/buildshots.sh    # + 30 close frames of the fight (motion in time)
# UNITY may point at another Editor install. Git Bash on Windows.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WROOT="$(cygpath -w "$ROOT" 2>/dev/null || echo "$ROOT")"
U="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.0.82f1/Editor/Unity.exe}"
OUT="$ROOT/tools/out"; mkdir -p "$OUT"
taskkill //IM ExcelHeroes.exe //F > /dev/null 2>&1
"$U" -batchmode -nographics -quit -projectPath "$WROOT" -executeMethod ExcelHeroes.EditorTools.BuildGame.Windows -logFile "$OUT/build.log"
rc=$?
if grep -q "error CS" "$OUT/build.log"; then echo "COMPILE ERRORS:"; grep "error CS" "$OUT/build.log" | sort -u | head -20; exit 1; fi
grep -E "\[BuildGame\]" "$OUT/build.log" | tail -2
[ $rc -ne 0 ] && { echo "build rc=$rc"; tail -30 "$OUT/build.log"; exit 1; }
rm -rf "$OUT/shots"; mkdir -p "$OUT/shots"
cd "$ROOT/Build/Windows" && timeout 300 ./ExcelHeroes.exe -screenshots "$(cygpath -w "$OUT/shots" 2>/dev/null || echo "$OUT/shots")" \
  -screen-width 1200 -screen-height 540 -screen-fullscreen 0 ${BURST:+-burst} > /dev/null 2>&1
echo "shots: $(ls "$OUT/shots" | wc -l) -> $OUT/shots"
