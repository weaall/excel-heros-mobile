# -*- coding: utf-8 -*-
"""
Hair variety across the cast (the user: "머리스타일이나 비슷한 것들이 너무 많다"). Gemini looks at a
contact sheet of every standing illustration (tools/out/hair_sheet.png, labelled by id) with each
hero's profile and persona, finds the look-alikes (the short brown bobs, the silver hair, the blue
bobs …) and plans NEW hairstyles and colours for the fewest characters that make every silhouette
distinct — only styles the 3D SD can build (World/SdSample hair library: short, bob, long, bun,
spiky, curly, ponytail, twin, side), realistic or softly anime colours.

    python tools/hair_diversity_gemini.py   → tools/out/hair_plan.json (id, style, colour, prompt, why)
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

DATA = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data")
STYLES = ["short", "bob", "long", "bun", "spiky", "curly", "ponytail", "twin", "side"]


def main():
    h = json.load(open(os.path.join(DATA, "heroes.json"), encoding="utf-8")); H = h["items"] if isinstance(h, dict) else h
    spec = json.load(open(os.path.join(DATA, "sdspec.json"), encoding="utf-8"))["items"]
    sp = {r["id"]: r for r in spec}
    cast = [{"id": x["id"], "name": x["name"], "gender": x.get("gender"), "grade": x.get("grade"), "dept": x.get("dept"), "nick": x.get("nick"),
             "persona": sp.get(x["id"], {}).get("persona", ""), "style_now": sp.get(x["id"], {}).get("style", ""), "hair_now": sp.get(x["id"], {}).get("hair", "")} for x in H]
    img = base64.b64encode(open(os.path.join(g.ROOT, "tools", "out", "hair_sheet.png"), "rb").read()).decode("ascii")
    text = ("The image is a contact sheet of every character in our office-worker hero game (Blue Archive style), labelled by id. "
            f"The cast as JSON (persona = their body-language archetype):\n{json.dumps(cast, ensure_ascii=False)}\n\n"
            "Players say too many of them look alike, above all the HAIR: several short brown bobs, many white/silver heads, "
            "several short blue bobs, several short black cuts. Plan new hairstyles for the FEWEST characters (about 14-18) so that "
            "every character reads as a different silhouette and palette at a glance. Rules: never change 'intern' (the protagonist) "
            f"or the men's short cuts into long hair; each new style must be one of {STYLES} (the 3D chibi can only build these — "
            "ponytail = a high or low single tail, twin = two tails, side = a side ponytail, bun = an updo or bun, curly = voluminous "
            "curls/waves) and you may add ONE small accessory (clip, ribbon, headband, scrunchie) that fits the person. Colours: "
            "natural office colours (black, dark brown, chestnut, auburn, honey blonde, ash brown, copper, burgundy) and only a few "
            "soft anime tints; cut the number of white/silver heads to at most 4. Fit each change to the person (rank, dept, persona). "
            "After your changes no style should be used by more than 9 characters. Answer JSON only: "
            '[{"id": "...", "style": "...", "hair": "#rrggbb", "desc": "a one-line hairstyle description for an illustrator, incl. length, '
            'fringe, parting, tail position, accessory", "why": "..."}]')
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": text}]}],
                       "generationConfig": {"temperature": 0.5, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": g.read_key()})
    with urllib.request.urlopen(req, timeout=500) as r: d = json.loads(r.read().decode("utf-8"))
    t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    plan = [r for r in json.loads(t) if r.get("id") != "intern" and r.get("style") in STYLES]
    json.dump(plan, open(os.path.join(g.ROOT, "tools", "out", "hair_plan.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    for r in plan: print(f'{r["id"]:14s} {sp.get(r["id"], {}).get("style", ""):8s}-> {r["style"]:8s} {r["hair"]}  {r["desc"]}')
    import collections
    after = {x["id"]: sp.get(x["id"], {}).get("style", "") for x in H}; after.update({r["id"]: r["style"] for r in plan})
    print(collections.Counter(after.values()))


if __name__ == "__main__":
    main()
