# -*- coding: utf-8 -*-
"""
The per-character SD model spec — the one file the 3D SD is built from, so a new or changed
illustration only needs its row regenerated:

    Resources/Data/sdspec.json   { items: [ { id, style, fringe, ahoge, glasses, sunglasses,
                                              hair, eye, skin, top, shirt, bottom, legs, shoes,
                                              outfit, bottomType, idle, win, attack, manual } ] }

Sources, merged in this order (later wins): defaults ← dolls.json (style, outfit, accessories,
fallback colours) ← looks.json (colours sampled from the character's own SD illustration by
tools/sample_looks.py) ← the existing spec's HAND-EDITED fields (listed in each row's `manual`).
So the pipeline can be re-run any time without losing a fringe or an idle someone chose by eye.

    python tools/sd_spec.py                 # every character
    python tools/sd_spec.py intern cfo      # some
    python tools/sd_spec.py --set intern fringe=2 idle=3     # a hand edit (recorded in `manual`)

fringe: 0 sample · 1 longer · 2 swept left · 3 swept right · 4 short, parted (-1 = by hash)
idle / win: 0..5 (-1 = by hash) — World/SdPose lists them.   attack: melee | ranged | caster | "" (= by role)
"""
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Data")
SPEC = os.path.join(DATA, "sdspec.json")
FIELDS = ["style", "fringe", "ahoge", "glasses", "sunglasses", "hair", "eye", "skin", "top", "shirt",
          "bottom", "legs", "shoes", "outfit", "bottomType", "idle", "win", "attack"]


def load(name):
    p = os.path.join(DATA, name)
    if not os.path.exists(p): return {}
    d = json.load(open(p, encoding="utf-8"))
    items = d["items"] if isinstance(d, dict) else d
    return {r["id"]: r for r in items if r.get("id")}


def hash_of(s):
    h = 17
    for c in s: h = (h * 31 + ord(c)) & 0xFFFFFFFF
    return h


def build_row(cid, doll, look, old):
    r = {"id": cid, "style": "short", "fringe": -1, "ahoge": False, "glasses": False, "sunglasses": False,
         "hair": "", "eye": "", "skin": "", "top": "", "shirt": "", "bottom": "", "legs": "", "shoes": "",
         "outfit": "suit", "bottomType": "pants", "idle": -1, "win": -1, "attack": "", "manual": []}
    if doll:
        acc = [a.split(":")[0] for a in doll.get("acc", [])]
        r.update(style=doll.get("hair", "short"), outfit=doll.get("outfit", "suit"), bottomType=doll.get("bottom", "pants"),
                 glasses="glasses" in acc, sunglasses="sunglasses" in acc,
                 hair=doll.get("hairColor", ""), eye=doll.get("eye", ""), top=doll.get("top", ""), shirt=doll.get("shirt", ""),
                 bottom=doll.get("bottomColor", ""))
        r["ahoge"] = (sum(ord(c) for c in cid) % 3) == 0
    if look:
        for k in ("hair", "eye", "skin", "top", "bottom", "legs", "shoes"):
            if look.get(k): r[k] = look[k]
    if cid == "intern": r["hair"] = "#1d1f2a"          # 김인턴: black hair, always (CLAUDE.md)
    if old:
        r["manual"] = list(old.get("manual", []))
        for k in r["manual"]:
            if k in old: r[k] = old[k]
    return r


def main(argv):
    dolls, looks = load("dolls.json"), load("looks.json")
    old = load("sdspec.json")
    if argv and argv[0] == "--set":
        cid = argv[1]
        row = old.get(cid) or build_row(cid, dolls.get(cid), looks.get(cid), None)
        for kv in argv[2:]:
            k, v = kv.split("=", 1)
            if k not in FIELDS: sys.exit(f"unknown field {k}")
            row[k] = (v.lower() == "true") if isinstance(row[k], bool) else int(v) if isinstance(row[k], int) else v
            if k not in row["manual"]: row["manual"].append(k)
        old[cid] = row
        rows = old
    else:
        ids = argv or sorted(set(dolls) | set(looks) | set(old))
        rows = dict(old)
        for cid in ids:
            rows[cid] = build_row(cid, dolls.get(cid), looks.get(cid), old.get(cid))
    out = {"items": [rows[k] for k in sorted(rows)]}
    json.dump(out, open(SPEC, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"sdspec.json: {len(out['items'])} characters")


if __name__ == "__main__":
    main(sys.argv[1:])
