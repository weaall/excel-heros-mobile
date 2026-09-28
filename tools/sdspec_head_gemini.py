# -*- coding: utf-8 -*-
"""
Each hero's head accessories (sdspec "head", World/SdHeadwear): Gemini's vision model lists what
the illustration wears in or on the hair, each with its colour and side —

    hairclip  a small clip / barrette / hairpins in the fringe
    headband  a band over the crown
    ribbon    a bow in the hair
    flower    a flower ornament
    beret     a beret / soft round hat
    cap       a peaked cap
    headset   headphones / a headset on the head
    neckphones headphones resting around the neck
    ahoge     a single upright strand of hair on the crown
    earring   earrings visible below the hair
    crown     a crown or tiara
    bandana   a kerchief tied over the hair

    python tools/sdspec_head_gemini.py            # every hero with a sample body
    python tools/sdspec_head_gemini.py cfo ceo
Written as "hairclip:#f5c542:left,ahoge". A row with "head" in "manual" is left alone.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sdspec_body_gemini as sb

KINDS = ["hairclip", "headband", "ribbon", "flower", "beret", "cap", "headset", "neckphones", "ahoge", "earring", "crown", "bandana"]
ASK = ("List the accessories this character wears ON THE HEAD OR IN THE HAIR (ignore glasses, clothes, badges, hand-held items). "
       f"Allowed kinds: {', '.join(KINDS)} ('ahoge' = a single upright/curled strand of hair sticking up from the crown). "
       "For each give its main colour as #rrggbb and its side: 'left' or 'right' (the CHARACTER's own left/right, i.e. the viewer's right is the character's left), "
       "'top', 'back' or 'both'. Only list what is clearly visible; an empty list is fine. "
       'Answer JSON only: {"items": [{"kind": "", "color": "#000000", "side": ""}], "why": ""}')


def ask(key, png):
    saved = sb.ASK; sb.ASK = ASK
    try: return sb.ask(key, png)
    finally: sb.ASK = saved


if __name__ == "__main__":
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    spec = json.load(open(sb.SPEC, encoding="utf-8")); key = g.read_key()
    for row in spec["items"]:
        hid = row["id"]
        if only and hid not in only: continue
        if "head" in (row.get("manual") or []) or not row.get("body"): continue
        png = os.path.join(sb.STAND, hid + ".png")
        if not os.path.exists(png): continue
        a = ask(key, png)
        if not a: print("  ?", hid); continue
        out = []
        for it in (a.get("items") or [])[:4]:
            k = it.get("kind")
            if k not in KINDS or any(o.startswith(k) for o in out): continue
            col = it.get("color") if re.fullmatch(r"#[0-9a-fA-F]{6}", str(it.get("color"))) else ""
            side = it.get("side") if it.get("side") in ("left", "right", "top", "back", "both") else ""
            out.append(":".join(x for x in (k, col, side) if x) if k != "ahoge" else "ahoge")
        row["head"] = ",".join(out)
        print(f"  {hid:16s} {row['head']:60s} {a.get('why', '')[:40]}")
        with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
