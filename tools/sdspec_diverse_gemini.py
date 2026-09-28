# -*- coding: utf-8 -*-
"""
Spreads the heroes over the sample tops and lowers (sdspec "body" / "lower") so no two heroes
wear the same combination and no sample is worn by half the cast: Gemini's vision model SCORES
every option 0-10 against the hero's illustration (cached in tools/out/sdscores.json), then an
assignment with a cap per option maximises the total score.

    python tools/sdspec_diverse_gemini.py            # score what is missing, assign, write
    python tools/sdspec_diverse_gemini.py --rescore  # ask again for everyone
    python tools/sdspec_diverse_gemini.py --dry
A row with "body" (or "lower") in "manual" keeps it. The intern (male) keeps the base body.
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import json, os, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
import sdspec_body_gemini as sb

CACHE = os.path.join(g.ROOT, "tools", "out", "sdscores.json")
ASK = ("You are dressing a 3D chibi version of this character from a small wardrobe. Score how well EACH option "
       "matches the character's clothes (0 = nothing alike, 10 = the same kind of garment). Judge silhouette and garment "
       "type (jacket / blazer / hoodie / dress, skirt length, tights or bare legs, footwear), not colours — colours are repainted.\n"
       "TOPS:\n" + "\n".join(f"- {k}: {v}" for k, v in sb.BODIES.items()) +
       "\nLOWERS (skirt / legs / shoes):\n" + "\n".join(f"- {k}: {v}" for k, v in sb.LOWERS.items()) +
       '\nAnswer JSON only: {"tops": {option: score, ...}, "lowers": {option: score, ...}}')


def score(key, png):
    saved = sb.ASK; sb.ASK = ASK
    try: return sb.ask(key, png)
    finally: sb.ASK = saved


def assign(heroes, scores, options, cap, fixed, step=0.15):
    """Hungarian on slots: each option repeated `cap` times; the n-th use of an option costs a little more."""
    from scipy.optimize import linear_sum_assignment
    import numpy as np
    slots = [(o, n) for o in options for n in range(cap)]
    cost = np.full((len(heroes), len(slots)), 1e6)
    for i, h in enumerate(heroes):
        for j, (o, n) in enumerate(slots):
            if h in fixed: cost[i, j] = 0 if o == fixed[h] else 1e6
            else: cost[i, j] = -float(scores[h].get(o, 0)) + step * n
    r, c = linear_sum_assignment(cost)
    return {heroes[i]: slots[j][0] for i, j in zip(r, c)}


if __name__ == "__main__":
    args = sys.argv[1:]
    spec = json.load(open(sb.SPEC, encoding="utf-8"))
    cache = json.load(open(CACHE, encoding="utf-8")) if os.path.exists(CACHE) else {}
    key = g.read_key()
    rows = [r for r in spec["items"] if r["id"] != "intern" and os.path.exists(os.path.join(sb.STAND, r["id"] + ".png")) and r.get("body")]
    for r in rows:
        if r["id"] in cache and "--rescore" not in args: continue
        a = score(key, os.path.join(sb.STAND, r["id"] + ".png"))
        if not a: print("  ?", r["id"]); continue
        cache[r["id"]] = {"tops": {k: float(v) for k, v in (a.get("tops") or {}).items() if k in sb.BODIES},
                          "lowers": {k: float(v) for k, v in (a.get("lowers") or {}).items() if k in sb.LOWERS}}
        print(f"  {r['id']:16s} {max(cache[r['id']]['tops'], key=cache[r['id']]['tops'].get, default='')}")
        json.dump(cache, open(CACHE, "w", encoding="utf-8"), indent=1)
    heroes = [r["id"] for r in rows if r["id"] in cache]
    byid = {r["id"]: r for r in rows}
    n = len(heroes)
    # jointly: a slot is a (top, lower) pair, so two heroes share a whole outfit only when the 54 pairs run out
    pairs = [f"{t}|{l}" for t in sb.BODIES for l in sb.LOWERS]
    joint = {h: {f"{t}|{l}": cache[h]["tops"].get(t, 0) + 0.7 * cache[h]["lowers"].get(l, 0) for t in sb.BODIES for l in sb.LOWERS} for h in heroes}
    fixed = {h: f"{byid[h]['body']}|{byid[h].get('lower') or byid[h]['body']}" for h in heroes if "body" in (byid[h].get("manual") or [])}
    pick = assign(heroes, joint, pairs, 3, fixed, step=1.2)
    tops = {h: pick[h].split("|")[0] for h in heroes}; lows = {h: pick[h].split("|")[1] for h in heroes}
    combos = {}
    for h in heroes:
        combos.setdefault((tops[h], lows[h]), []).append(h)
    for h in heroes:
        byid[h]["body"] = tops[h]
        byid[h]["lower"] = "" if lows[h] == tops[h] else lows[h]
        print(f"  {h:16s} {tops[h]:22s} {lows[h]}")
    from collections import Counter
    print("tops", Counter(tops.values())); print("lowers", Counter(lows.values()))
    print("combos", len(combos), "shared:", [v for v in combos.values() if len(v) > 1])
    if "--dry" not in args:
        with open(sb.SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
