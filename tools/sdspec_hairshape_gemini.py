# -*- coding: utf-8 -*-
"""
The hero's own hair volume, fall and tails on top of the part recipe (sdspec "hairParts" vol= / fall= / tails=,
World/SdRefHairLib.Volume/Fall): Gemini's vision model reads from the illustration how full the
hair is and, for long hair, how far down it falls.

    python tools/sdspec_hairshape_gemini.py            # every hero with a recipe
    python tools/sdspec_hairshape_gemini.py cfo ceo
A row with "hairParts" in "manual" is left alone.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sdspec_body_gemini as sb

ASK = ("Look at this character's HAIR only. Answer JSON only: "
       '{"volume": number, "fall": number, "tails": "", "wave": number, "spread": number, "gather": number, "curl": number, "why": ""}. '
       "volume: how full / puffy the hair is around the head — 0.92 = sleek and flat, 1.0 = normal, 1.08 = full, 1.15 = very fluffy or wavy. "
       "fall: how far down long hair hangs — 0.85 = shoulder blades, 1.0 = mid-back, 1.15 = waist, 1.3 = hips or longer "
       "(for short hair answer 1.0). "
       "tails: 'twin' for twin tails / pigtails tied high, 'longtwin' for very long twin tails reaching the legs, 'side' for ONE side ponytail, "
       "'none' otherwise (a bun, a ponytail at the back, or loose hair are 'none'). "
       "For hair below the chin also: wave 0 = straight, 0.5 = wavy, 1 = curly; spread -0.7 = the ends fall close together, 0 = natural, 0.8 = they fan out wide; "
       "gather 0 = loose, 0.6 = loosely tied low, 1 = a low ponytail at the nape; curl 0.8 = the ends turn under / inward, -0.8 = they flick outward, 0 = straight ends.")
TAILS = {"twin": "reisa", "longtwin": "miku", "side": "natsu"}


def ask(key, png):
    saved = sb.ASK; sb.ASK = ASK
    try: return sb.ask(key, png)
    finally: sb.ASK = saved


if __name__ == "__main__":
    only = [a for a in sys.argv[1:] if not a.startswith("--")]
    spec = json.load(open(sb.SPEC, encoding="utf-8")); key = g.read_key()
    for row in spec["items"]:
        hid = row["id"]; rec = row.get("hairParts") or ""
        if only and hid not in only: continue
        if not rec or "hairParts" in (row.get("manual") or []): continue
        png = os.path.join(sb.STAND, hid + ".png")
        if not os.path.exists(png): continue
        a = ask(key, png)
        if not a: print("  ?", hid); continue
        def num(v, lo, hi):
            try: return max(lo, min(hi, float(v)))
            except (TypeError, ValueError): return None
        vol = num(a.get("volume"), 0.9, 1.18); fall = num(a.get("fall"), 0.8, 1.3)
        rec = re.sub(r";?(vol|fall|tails|wave|spread|gather|curl)=[^;]*", "", rec)
        if a.get("tails") in TAILS: rec += f";tails={TAILS[a['tails']]}"
        if vol is not None: rec += f";vol={vol:.2f}"
        if fall is not None and "len=long" in rec: rec += f";fall={fall:.2f}"
        if "len=long" in rec or "len=shoulder" in rec:
            for k2, lo, hi in (("wave", 0, 1.2), ("spread", -1, 1), ("gather", 0, 1), ("curl", -1, 1)):
                x = num(a.get(k2), lo, hi)
                if x is not None and abs(x) >= 0.1: rec += f";{k2}={x:.2f}"
        row["hairParts"] = rec
        print(f"  {hid:16s} {rec}")
        with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
