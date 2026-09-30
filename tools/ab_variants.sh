#!/bin/bash
# A/B env variants against the baseline capture (tools/out/shots2) without rebuilding:
#   bash tools/ab_variants.sh "name:EH_SHADE=0.6" "name2:EH_PITCH=32 EH_DIST=10"   [BUILD=Windows3]
# Each variant is captured to tools/out/v_<name> (reused if present; -shotsuntil 08-Party) and judged by
# tools/ab_judge.py on Fight2/3/4 (squad crop) + Win, n=3 → x/24. Captures are deterministic
# (ScreenshotDriver: captureFramerate + seed), but the judge is not: identical images score 7–17/24,
# so only ≥18/24 (or ≤6/24) is a signal.
ROOT=/c/Users/minds/excel-heros-mobile; cd $ROOT
for spec in "$@"; do
  n=${spec%%:*}; e=${spec#*:}; out=$ROOT/tools/out/v_$n
  if [ ! -f $out/07-Win.png ]; then rm -rf $out; mkdir -p $out
    (cd Build/${BUILD:-Windows3} && env $e timeout 300 ./ExcelHeroes.exe -screenshots "C:\Users\minds\excel-heros-mobile\tools\out\v_$n" -shotsuntil 08-Party -screen-width 2400 -screen-height 1080 -screen-fullscreen 0 > /dev/null 2>&1); fi
  r=""; t=0
  for f in 07-Fight2 07-Fight3 07-Fight4; do
    w=$(python tools/ab_judge.py tools/out/shots2/$f.png $out/$f.png tools/out/ba_ref/ref02.png "the SD squad fighting" --n 3 --crop 0.05,0.25,0.55,0.95 | head -1 | awk '{print $3}'); r="$r $f=$w"; t=$((t+${w%%/*}))
  done
  w=$(python tools/ab_judge.py tools/out/shots2/07-Win.png $out/07-Win.png tools/out/ba_ref/ref04.png "the battle result" --n 3 | head -1 | awk '{print $3}'); r="$r win=$w"; t=$((t+${w%%/*}))
  echo "$n [$e]$r total=$t/24"
done
