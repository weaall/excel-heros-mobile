# -*- coding: utf-8 -*-
"""
The item each hero holds (sdspec "prop", World/SdRefProps.HandProp): Gemini's vision model picks,
from the illustration and the hero's job, the one hand-held office item that suits her best —
document, cup, clipboard, tablet, phone, pen, folder, calculator, laptop.

    python tools/sdspec_prop_gemini.py            # every hero with a sample body
A row with "prop" in "manual" is left alone.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sdspec_body_gemini as sb

KINDS = ["document", "cup", "clipboard", "tablet", "phone", "pen", "folder", "calculator", "laptop"]
ASK = ("This is an office worker character (her job id is '{job}'). Which ONE hand-held item suits her best? "
       "If she holds something in the illustration, that; otherwise what her job would carry. "
       f"Options: {', '.join(KINDS)}. Answer JSON only: " + '{"prop": "", "why": ""}')

if __name__ == "__main__":
    spec = json.load(open(sb.SPEC, encoding="utf-8")); key = g.read_key()
    from collections import Counter; cnt = Counter()
    for row in spec["items"]:
        hid = row["id"]
        if "prop" in (row.get("manual") or []) or not row.get("body"): continue
        png = os.path.join(sb.STAND, hid + ".png")
        if not os.path.exists(png): continue
        saved = sb.ASK; sb.ASK = ASK.replace("{job}", hid)
        try: a = sb.ask(key, png)
        finally: sb.ASK = saved
        if not a or a.get("prop") not in KINDS: print("  ?", hid); continue
        row["prop"] = a["prop"]; cnt[a["prop"]] += 1
        print(f"  {hid:16s} {row['prop']:10s} {a.get('why', '')[:60]}")
    with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
    print(cnt)
