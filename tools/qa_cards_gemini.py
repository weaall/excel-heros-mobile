# -*- coding: utf-8 -*-
"""
Automatic review of the v2 illustrations with Gemini's vision (text answer, no image made): for
each card it asks what a person checking the set by eye would — does she read as a woman, as an
adult, is there a halo left, is the face / hands broken — and writes the verdicts to
tools/out/qa_cards.json plus the ids to redo (tools/out/qa_redo.txt).

    python tools/qa_cards_gemini.py                  # every ArtSource/Cards_v2 png
    python tools/qa_cards_gemini.py --dir ArtSource/Standing_v2/raw

Why: the cast is all women, but several of the old cards had drawn them as boys, and "keep the
identity" carried that into v2. A reviewer that reads every card is cheaper than a person
paging through 66, and it is the same question every time.
SECURITY: the key is read from the env file by NAME and sent in a header; never printed or written.
"""
import base64, json, os, sys, time, urllib.request, urllib.error
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("QA_MODEL", "gemini-3.8-flash")
ASK = (
    "You are reviewing one character illustration for a mobile game. The intended character is {who}. Answer ONLY with JSON: "
    '{{"reads_as": "woman" | "man" | "unclear", "adult": true|false, "school_uniform": true|false, "halo": true|false, "broken_face_or_hands": true|false, '
    '"pretty": 1-5, "note": "<10 words>"}}. "reads_as" is how a typical viewer would read the gender at a glance.'
)


def ask(key, path, who):
    with open(path, "rb") as f: img = base64.b64encode(f.read()).decode("ascii")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": img}}, {"text": ASK.format(who=who)}]}],
                       "generationConfig": {"responseMimeType": "application/json", "temperature": 0}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=120) as r: data = json.loads(r.read().decode("utf-8"))
    text = "".join(p.get("text", "") for c in data.get("candidates", []) for p in c.get("content", {}).get("parts", []))
    return json.loads(text)


if __name__ == "__main__":
    args = sys.argv[1:]
    d = args[args.index("--dir") + 1] if "--dir" in args else os.path.join(g.ROOT, "ArtSource", "Cards_v2")
    cast = g.cast(); key = g.read_key()
    results, redo = {}, []
    for f in sorted(os.listdir(d)):
        if not f.endswith(".png"): continue
        i = f[:-4]; grade, sex = cast.get(i, ("D", "F"))
        who = "a young adult woman (an office worker)" if sex == "F" else "a young adult man (an office worker)"
        try: v = ask(key, os.path.join(d, f), who)
        except Exception as e: print(f"  {i}: error {str(e)[:80]}"); continue
        results[i] = v
        want = "woman" if sex == "F" else "man"
        bad = v.get("reads_as") != want or not v.get("adult", True) or v.get("school_uniform") or v.get("halo") or v.get("broken_face_or_hands")
        if bad: redo.append(i)
        print(f"  {'REDO' if bad else 'ok  '} {i:16} {v.get('reads_as')} adult={v.get('adult')} school={v.get('school_uniform')} halo={v.get('halo')} broken={v.get('broken_face_or_hands')} pretty={v.get('pretty')} {v.get('note', '')}")
        time.sleep(0.5)
    out = os.path.join(g.ROOT, "tools", "out"); os.makedirs(out, exist_ok=True)
    with open(os.path.join(out, "qa_cards.json"), "w", encoding="utf-8") as f: json.dump(results, f, ensure_ascii=False, indent=1)
    with open(os.path.join(out, "qa_redo.txt"), "w", encoding="utf-8") as f: f.write("\n".join(redo))
    print(f"{len(redo)} to redo: {' '.join(redo)}")
