# -*- coding: utf-8 -*-
"""The hair recipes closest to each other by number (parts differ = 1 each, knobs weighted):
prints "ids" and "pairs" lines for tools/sd_hairdistinct_gemini.py. python tools/sd_hairclose.py [threshold]"""
import json, itertools, os, sys
SPEC = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json")
PARTS = ("front", "side", "back", "extra", "tails", "len")
NUMS = {"vol": (3, 1), "fall": (2, 1), "wave": (1, 0), "spread": (1, 0), "gather": (1.5, 0), "curl": (1, 0), "slant": (1, 0)}

def recipes():
    out = {}
    for r in json.load(open(SPEC, encoding="utf-8"))["items"]:
        if not r.get("hairParts"): continue
        d = dict(x.split("=") for x in r["hairParts"].split(";") if "=" in x)
        if d.get("front") == "yuuka" or d.get("back") == "yuuka": d["side"] = "yuuka"
        out[r["id"]] = d
    return out

def dist(a, b):
    x = sum(1 for p in PARTS if a.get(p, "none") != b.get(p, "none"))
    return x + sum(w * abs(float(a.get(k, dv)) - float(b.get(k, dv))) for k, (w, dv) in NUMS.items())

if __name__ == "__main__":
    th = float(sys.argv[1]) if len(sys.argv) > 1 else 0.8
    R = recipes(); used = set(); pairs = []
    for d, a, b in sorted((dist(R[a], R[b]), a, b) for a, b in itertools.combinations(R, 2)):
        if d >= th: break
        if a in used or b in used: continue
        used |= {a, b}; pairs.append((a, b))
    print("ids", ",".join(sorted(used)))
    print("pairs", ",".join(f"{a}:{b}" for a, b in pairs))
