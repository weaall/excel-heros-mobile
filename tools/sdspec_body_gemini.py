# -*- coding: utf-8 -*-
"""
Which sample body each hero wears (sdspec.json "body", World/SdSample): Gemini's vision model
looks at the hero's standing illustration and picks the sample outfit closest to it from the
nine, by description. The protagonist (male) and anything judged male keep the base ("").
A row with "body" in its "manual" list is left alone.

    python tools/sdspec_body_gemini.py              # every hero with a standing illustration
    python tools/sdspec_body_gemini.py cfo ceo      # these
    python tools/sdspec_body_gemini.py --dry
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("QA_MODEL", "gemini-3.8-flash")
SPEC = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json")
STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
BODIES = {
    "hayase_yuuka": "smart uniform: a long white jacket worn open over a black dress, a necktie, a short pleated skirt, boots — a sharp office / secretary look",
    "kayoko_dress_ver_": "a black strapless mini dress with a beige jacket worn off the shoulders — a glamorous executive / evening look",
    "haruka": "a dark sleeveless knee-length dress, bare legs, flats — an elegant, gentle dress look",
    "hikari": "a navy military-style buttoned uniform jacket with a pleated navy skirt and white tights — a formal uniform / security / official look",
    "yutori_natsu": "a white blazer with a sailor collar and a red ribbon, a pleated skirt, knee socks — a student / intern / junior look",
    "reisa": "a navy zip hoodie jacket over a sailor top, a grey pleated skirt, sneakers — a casual / tech / sporty look",
    "mika": "a white frilly princess dress with ribbons — a luxurious, top-rank noble look",
    "hatsune_miku": "a sleeveless white idol top, a frilly layered skirt, detached sleeves — an idol / performer look",
    "hayase_yuuka_gym_ver_": "a gym jacket and gym shorts — a sporty / fitness look",
}
ASK = ("You are matching a character illustration to the closest 3D outfit available. Options:\n" +
       "\n".join(f"- {k}: {v}" for k, v in BODIES.items()) +
       "\nLook at the character's clothes (silhouette: jacket or not, dress or skirt or trousers, formality, colours matter less). "
       "Answer JSON only: {\"male\": true|false, \"body\": one option key, \"why\": a few words}.")


def ask(key, path):
    with open(path, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK}]}],
                       "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r: data = json.loads(r.read().decode("utf-8"))
            return json.loads(data["candidates"][0]["content"]["parts"][0]["text"])
        except (urllib.error.HTTPError, urllib.error.URLError, KeyError, json.JSONDecodeError) as e:
            print(f"    retry ({type(e).__name__})"); time.sleep(3)
    return None


if __name__ == "__main__":
    args = sys.argv[1:]; dry = "--dry" in args
    only = [a for a in args if not a.startswith("--")]
    spec = json.load(open(SPEC, encoding="utf-8"))
    key = g.read_key(); counts = {}
    for row in spec["items"]:
        hid = row["id"]
        if only and hid not in only: continue
        if "body" in (row.get("manual") or []): continue
        png = os.path.join(STAND, hid + ".png")
        if not os.path.exists(png): continue
        a = ask(key, png)
        if a is None: print(f"  ?  {hid}"); continue
        pick = "" if hid == "intern" or a.get("male") else (a.get("body") if a.get("body") in BODIES else "")
        row["body"] = pick
        counts[pick or "(base)"] = counts.get(pick or "(base)", 0) + 1
        print(f"  {hid:16s} -> {pick or '(base)':22s} {a.get('why', '')}")
    if not dry:
        with open(SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
    print("counts", counts)
