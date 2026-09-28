#!/bin/bash
# A/B for hair edits: every spec row with "hairPrev" (set by sd_hairfix / sd_hairdistinct) is
# rendered and scored again; the new recipe stays only if its hair likeness is not worse.
#   bash tools/sd_hair_ab.sh
cd "$(dirname "$0")/.."
cp tools/out/likeness.json tools/out/likeness_before_ab.json
IDS=$(python -c "
import json;s=json.load(open('Assets/ExcelHeroes/Resources/Data/sdspec.json',encoding='utf-8'));print(','.join(r['id'] for r in s['items'] if 'hairPrev' in r))")
[ -z "$IDS" ] && { echo "nothing to test"; exit 0; }
SD_IDS=$IDS SD_PREVIEW_OUT="$(cygpath -w tools/out/hairab)" bash tools/unity.sh ExcelHeroes.EditorTools.SdBasePreview.Run hairab > /dev/null
PYTHONIOENCODING=utf-8 python tools/sd_likeness_gemini.py tools/out/hairab $IDS > /dev/null
python - <<'PY'
import json
old=json.load(open("tools/out/likeness_before_ab.json",encoding="utf-8")); new=json.load(open("tools/out/likeness.json",encoding="utf-8"))
P="Assets/ExcelHeroes/Resources/Data/sdspec.json"; sp=json.load(open(P,encoding="utf-8"))
for r in sp["items"]:
    h=r["id"]
    if "hairPrev" not in r: continue
    o=old.get(h,{}).get("hair",0); n=new.get(h,{}).get("hair",0)
    if n < o: r["hairParts"]=r["hairPrev"]; new[h]=old[h]; v="revert"
    else: v="keep"
    del r["hairPrev"]; print(f"  {h:16s} hair {o} -> {n}  {v}")
json.dump(sp,open(P,"w",encoding="utf-8",newline="\n"),ensure_ascii=False,indent=1)
json.dump(new,open("tools/out/likeness.json","w",encoding="utf-8"),ensure_ascii=False,indent=1)
PY
