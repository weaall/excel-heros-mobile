# -*- coding: utf-8 -*-
"""
The BA-normalized cross-check score (the user's criterion, 2026-09-30): on the anchored judge
(tools/ba_crosscheck.py, BA = 10) Blue Archive's OWN screens score only ~7, so a raw 9 is unreachable.
Each pair is scored as ours ÷ BA's own score on the same judge × 10 (BA's from CALIBRATE=auto →
tools/out/ba_calib.json), and the blind side-by-side as ours ÷ BA × 10. Pass: ≥ 9.

    REPEAT=3 python tools/ba_crosscheck.py                  (ours → ba_crosscheck.json)
    CALIBRATE=auto REPEAT=3 python tools/ba_crosscheck.py   (BA → ba_calib.json, once)
    python tools/ba_normalized.py
SECURITY: none (local files only).
"""
import json, os

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "tools", "out")
AXES = ["graphics", "layout", "ui", "motion"]


def mean(r): return sum(float(r.get(k) or 0) for k in AXES) / len(AXES)


if __name__ == "__main__":
    ours = json.load(open(os.path.join(OUT, "ba_crosscheck.json"), encoding="utf-8"))
    cal = json.load(open(os.path.join(OUT, "ba_calib.json"), encoding="utf-8"))
    rows = []
    for n in ours:
        if n not in cal: continue
        o, b = mean(ours[n]), mean(cal[n])
        norm = min(10.0, o / max(0.1, b) * 10)
        blind = min(10.0, ours[n]["blind_ours"] / max(0.1, ours[n]["blind_ba"]) * 10)
        rows.append((n, o, b, norm, blind))
    print(f"{'pair':10s} {'ours':>5s} {'BA':>5s} {'norm':>5s} {'blind':>6s}")
    for n, o, b, norm, blind in sorted(rows, key=lambda r: r[3]):
        print(f"{n:10s} {o:5.2f} {b:5.2f} {norm:5.1f} {blind:6.1f}  {'PASS' if norm >= 9 else ''}")
    if rows: print(f"{'MEAN':10s} {'':5s} {'':5s} {sum(r[3] for r in rows) / len(rows):5.1f} {sum(r[4] for r in rows) / len(rows):6.1f}")
