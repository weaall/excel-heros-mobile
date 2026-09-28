# -*- coding: utf-8 -*-
"""
Glasses on the 3D SD follow the CURRENT illustration. sdspec.json was read off the first set of
illustrations; the v2 set (tools/gen_cards_gemini.py / standing_v2.sh) redrew everyone, and some
lost or gained glasses — the SD kept the old answer, so members with bare faces wore frames.

    python tools/sdspec_glasses_gemini.py          # every hero with a standing illustration
    python tools/sdspec_glasses_gemini.py cfo ceo  # these
    python tools/sdspec_glasses_gemini.py --dry    # report only

Gemini's vision model looks at Resources/Art/Standing/<id>.png and answers glasses / sunglasses /
frame style / frame colour; sdspec.json is updated in place (a field listed in the row's "manual"
is left alone). SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("QA_MODEL", "gemini-3.8-flash")
SPEC = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "sdspec.json")
STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
ASK = ("Look at this character illustration. Does the character wear glasses on the face (not held, not on the head)? "
       "Answer JSON only: {\"glasses\": true|false, \"sunglasses\": true|false, "
       "\"style\": one of square|round|oval|half|cat|rimless (the frame shape, or \"\" if none), "
       "\"color\": the frame colour as #rrggbb (or \"\" if none)}.")


def ask(key, path):
    with open(path, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK}]}],
                       "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r: data = json.loads(r.read().decode("utf-8"))
            text = data["candidates"][0]["content"]["parts"][0]["text"]
            return json.loads(text)
        except (urllib.error.HTTPError, urllib.error.URLError, KeyError, json.JSONDecodeError) as e:
            print(f"    retry ({type(e).__name__})"); time.sleep(3)
    return None


if __name__ == "__main__":
    args = sys.argv[1:]; dry = "--dry" in args
    only = [a for a in args if not a.startswith("--")]
    spec = json.load(open(SPEC, encoding="utf-8"))
    rows = spec["items"]
    key = g.read_key(); changed = 0
    for row in rows:
        hid = row["id"]
        if only and hid not in only: continue
        png = os.path.join(STAND, hid + ".png")
        if not os.path.exists(png): continue
        manual = set(row.get("manual") or [])
        a = ask(key, png)
        if a is None: print(f"  ?    {hid}: no answer"); continue
        gl, sg = bool(a.get("glasses")), bool(a.get("sunglasses"))
        before = (row.get("glasses"), row.get("sunglasses"))
        if "glasses" not in manual: row["glasses"] = gl and not sg
        if "sunglasses" not in manual: row["sunglasses"] = sg
        if row["glasses"] or row["sunglasses"]:
            if a.get("style") and "glassesStyle" not in manual: row["glassesStyle"] = a["style"]
            if a.get("color") and "glassesColor" not in manual: row["glassesColor"] = a["color"]
        after = (row.get("glasses"), row.get("sunglasses"))
        mark = "CHG " if after != before else "    "
        if after != before: changed += 1
        print(f"  {mark}{hid}: glasses {before[0]}->{after[0]}  sunglasses {before[1]}->{after[1]}  {a.get('style', '')} {a.get('color', '')}")
    if not dry:
        with open(SPEC, "w", encoding="utf-8", newline="\n") as f: json.dump(spec, f, ensure_ascii=False, indent=1)
    print(f"done: {changed} changed{' (dry run)' if dry else ''}")
