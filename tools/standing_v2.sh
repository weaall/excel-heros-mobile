#!/bin/bash
# Standing art v2, end to end, on THIS repo's folders (the web repo's tools are only run, never
# written to): raw (tools/gen_standing_gemini.py) → alpha (cutout_ai.py: two local background
# models that must agree, the BiRefNet Space as tie-break) → uniform (uniform.py std: one body for
# the cast — the same head size and height, 6 heads, feet on y 1318 of 768x1344) → Resources.
#
#   bash tools/standing_v2.sh            # every raw figure not cut out yet
#   FORCE=1 bash tools/standing_v2.sh    # redo the cut-out too
#
# HF: the Space call is anonymous (that Space refuses token-bearing calls); the model downloads
# (imgutils, rembg) take HF_TOKEN from the env file's first HUGGING_FACE_API_KEY_* if unset.
set -u
cd "$(dirname "$0")/.."
WEB=${WEB_REPO:-../excel-heros}
S=ArtSource/Standing_v2
mkdir -p "$S/alpha" "$S/uniform"
if [ -z "${HF_TOKEN:-}" ]; then
  for f in "${ENV_FILE:-}" /c/Users/minds/Desktop/mindsai_weaall.env; do
    [ -f "$f" ] && HF_TOKEN=$(grep -m1 '^HUGGING_FACE_API_KEY_' "$f" | cut -d= -f2- | tr -d '\r"'"'") && export HF_TOKEN && break
  done
fi
todo=()
for f in "$S"/raw/*.png; do
  id=$(basename "$f" .png)
  { [ -n "${FORCE:-}" ] || [ ! -f "$S/alpha/$id.png" ]; } && todo+=("$f")
done
if [ ${#todo[@]} -gt 0 ]; then
  python -W ignore "$WEB/tools/cutout_ai.py" "${todo[@]}" --out "$S/alpha" 2>&1 | tr '\r' '\n' | grep -v '%|'
fi
python -W ignore "$WEB/tools/uniform.py" std "$S/alpha" "$S/uniform" 2>&1 | tr '\r' '\n' | grep -v '%|'
n=0
for f in "$S"/uniform/*.png; do cp "$f" Assets/ExcelHeroes/Resources/Art/Standing/; n=$((n+1)); done
echo "[standing_v2] $n figures → Resources/Art/Standing"
