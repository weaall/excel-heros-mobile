# -*- coding: utf-8 -*-
"""
Each hero's garment shape (sdspec "garment", World/SdGarment): Gemini's vision model reads the
illustration and answers how to re-cut the sample outfit —

    pants   none | slim | wide          trousers, and their cut
    skirt   0.6 (mini) .. 1.0 (above the knee) .. 1.7 (below the knee) .. 2.2 (long)
    flare   -0.3 (pencil) .. 0 .. 0.6 (full, pleated / A-line)
    legs    bare | socks | tights       under a skirt

    python tools/sdspec_garment_gemini.py            # every hero with a sample body
    python tools/sdspec_garment_gemini.py cfo ceo
A row with "garment" in "manual" is left alone.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sdspec_body_gemini as sb

ASK = ("Describe this character's LOWER garment for re-cutting a 3D outfit. Answer JSON only: "
       '{"pants": "none" | "slim" | "wide", "skirt": number, "flare": number, "legs": "bare" | "socks" | "tights", "legcolor": "#rrggbb", "shoecolor": "#rrggbb", "why": ""}. '
       "pants: 'slim' for fitted trousers / slacks / jeans, 'wide' for wide-leg or baggy trousers, 'none' for a skirt or dress. "
       "skirt (only when pants is none): the hem length, 0.6 = micro-mini, 1.0 = mid-thigh, 1.4 = just above the knee, "
       "1.8 = below the knee, 2.2 = ankle length. flare: -0.3 = tight pencil skirt, 0 = straight, 0.3 = A-line, 0.6 = full / pleated circle. "
       "legs: 'tights' for pantyhose / stockings on the whole leg, 'socks' for knee or calf socks, 'bare' otherwise. "
       "legcolor: the colour of the tights or socks as they LOOK (sheer black tights over skin look dark grey-brown). shoecolor: the shoes' colour.")


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
        if "garment" in (row.get("manual") or []) or not row.get("body"): continue
        png = os.path.join(sb.STAND, hid + ".png")
        if not os.path.exists(png): continue
        a = ask(key, png)
        if not a: print("  ?", hid); continue
        pants = a.get("pants") if a.get("pants") in ("none", "slim", "wide") else "none"
        def num(v, lo, hi, fb):
            try: return max(lo, min(hi, float(v)))
            except (TypeError, ValueError): return fb
        legs = a.get("legs") if a.get("legs") in ("bare", "socks", "tights") else "bare"
        row["garment"] = f"pants={pants}" if pants != "none" else \
            f"pants=none;skirt={num(a.get('skirt'), 0.6, 2.2, 1.0):.2f};flare={num(a.get('flare'), -0.3, 0.6, 0.0):.2f};legs={legs}"
        row["bottomType"] = "pants" if pants != "none" else "skirt"
        # the tights' / socks' and shoes' own colours (the sampled ones were often the skirt's)
        if re.fullmatch(r"#[0-9a-fA-F]{6}", str(a.get("legcolor"))) and legs != "bare": row["legs"] = a["legcolor"]
        if re.fullmatch(r"#[0-9a-fA-F]{6}", str(a.get("shoecolor"))): row["shoes"] = a["shoecolor"]
        print(f"  {hid:16s} {row['garment']:50s} {a.get('why', '')[:50]}")
        with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
