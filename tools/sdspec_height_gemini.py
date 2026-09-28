# -*- coding: utf-8 -*-
"""
Each hero's height against the cast (sdspec "scale", World/SdSample: the whole figure scaled, feet
kept on the floor): Gemini's vision model reads the illustration's build — scored 1-10 and spread by rank over
0.94–1.05 — so the lineup is not 55 dolls of one size.

    python tools/sdspec_height_gemini.py            # every hero with a sample body
A row with "scale" in "manual" is left alone.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sdspec_body_gemini as sb

ASK = ("Judge this character's build as drawn (proportions, how long the legs and torso look, how mature she looks — "
       "not the picture's size) on a 1-10 scale: 1 = small and girlish, 5 = average young woman, 10 = tall, long-legged, mature. "
       "Answer JSON only: {\"height\": number, \"why\": \"\"}.")

if __name__ == "__main__":
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    spec = json.load(open(sb.SPEC, encoding="utf-8")); key = g.read_key(); scores = {}
    for row in spec["items"]:
        hid = row["id"]
        if only and hid not in only: continue
        if "scale" in (row.get("manual") or []) or not row.get("body"): continue
        png = os.path.join(sb.STAND, hid + ".png")
        if not os.path.exists(png): continue
        saved = sb.ASK; sb.ASK = ASK
        try: a = sb.ask(key, png)
        finally: sb.ASK = saved
        if not a: print("  ?", hid); continue
        try: scores[hid] = float(a.get("height"))
        except (TypeError, ValueError): continue
    # by rank, not by the raw score (the model calls most office women tall): 0.94 .. 1.05 across the cast
    order = sorted(scores, key=lambda h: (scores[h], h))
    for i, hid in enumerate(order):
        row = next(r for r in spec["items"] if r["id"] == hid)
        row["scale"] = round(0.94 + 0.11 * i / max(1, len(order) - 1), 3)
        print(f"  {hid:16s} {scores[hid]:.0f} -> {row['scale']}")
    with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
