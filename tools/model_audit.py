# -*- coding: utf-8 -*-
"""
3D model defect audit. Two modes:

  python tools/model_audit.py pair A_DIR B_DIR      # per hero: which build (A or B) has fewer mesh defects
  python tools/model_audit.py scan DIR              # per hero: list the mesh defects of one build

The images are SdBasePreview sheets (row 1 five turns, row 2 face close-ups, row 3 idle · ready ·
attack · victory · walk). Gemini is asked ONLY about construction defects — clipping (top through
skirt, legs through skirt), jagged / torn hems, spikes, holes, stray white or black shards, floating
parts, missing eyes, hair clipping through the body — never about style. Results: tools/out/model_audit.json.
SECURITY: the key is read from the env file by NAME only.
"""
import base64, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

MODEL = os.environ.get("AUDIT_MODEL", "gemini-3.1-pro-preview")
OUT = os.path.join(g.ROOT, "tools", "out", "model_audit.json")
DEFECTS = ("construction defects only: clothing clipping (the top poking through the skirt or trousers, legs or "
           "underwear poking through the skirt), jagged or torn hems, triangular spikes or teeth, holes, stray "
           "white or black shards, floating or detached parts, missing or broken eyes, hair clipping through the body")


def post(key, parts):
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=300) as r: d = json.loads(r.read().decode("utf-8"))
    text = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    r = json.loads(text)
    if isinstance(r, list): r = r[0] if r and isinstance(r[0], dict) and ('better' in r[0] or 'cleaner' in r[0]) else {"defects": r}
    return r


def img(path):
    with open(path, "rb") as f: return {"inlineData": {"mimeType": "image/png", "data": base64.b64encode(f.read()).decode("ascii")}}


def pair(key, a, b, hero):
    ask = (f"Two renders of the same 3D chibi character ({hero}), build A (image 1) and build B (image 2). Each sheet: row 1 "
           f"five turns, row 2 face close-ups, row 3 idle, ready, attack, victory, walk poses. Judge {DEFECTS}. Ignore style, "
           "colour and outfit design differences. Return JSON only: {\"a_defects\": [short strings], \"b_defects\": [short "
           "strings], \"cleaner\": \"A\" | \"B\" | \"same\"}")
    return post(key, [img(a), img(b), {"text": ask}])


def pair_ref(key, a, b, ref, hero):
    ask = (f"Image 1 is the character illustration of {hero}. Images 2 and 3 are two 3D chibi builds of that character, "
           f"A and B (sheets: row 1 five turns, row 2 faces, row 3 poses). Judge (1) {DEFECTS}, and (2) how faithfully the "
           "outfit's colours and main pieces match the illustration. Pick the build a player would accept as this "
           "character with no visibly broken mesh; a broken mesh outweighs a small colour mismatch. Return JSON only: "
           "{\"a_defects\": [..], \"b_defects\": [..], \"a_fidelity\": 1-10, \"b_fidelity\": 1-10, \"better\": \"A\" | \"B\"}")
    return post(key, [img(ref), img(a), img(b), {"text": ask}])


def scan(key, a, hero):
    ask = (f"A render sheet of one 3D chibi character ({hero}): row 1 five turns, row 2 face close-ups, row 3 idle, ready, "
           f"attack, victory, walk poses. List {DEFECTS}. Only real, visible defects; none is a valid answer. Return JSON only: "
           "{\"defects\": [{\"where\": \"...\", \"what\": \"...\", \"severity\": \"high|medium|low\"}]}")
    return post(key, [img(a), {"text": ask}])


if __name__ == "__main__":
    mode = sys.argv[1]; key = g.read_key(); sys.stdout.reconfigure(encoding="utf-8")
    if mode == "ref":
        da, db = sys.argv[2], sys.argv[3]
        cards = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Cards")
        heroes = sorted(f[:-4] for f in os.listdir(da) if f.endswith(".png") and os.path.exists(os.path.join(db, f)) and os.path.exists(os.path.join(cards, f)))
        def one(h):
            err = ""
            for _ in range(2):
                try: return h, pair_ref(key, os.path.join(da, h + ".png"), os.path.join(db, h + ".png"), os.path.join(cards, h + ".png"), h)
                except Exception as e: err = str(e)[:80]
            return h, {"better": "error", "a_defects": [err], "b_defects": []}
        res = {}
        with cf.ThreadPoolExecutor(6) as ex:
            for h, r in ex.map(one, heroes):
                res[h] = r; print(f"{h:16s} {r.get('better')}  A def {len(r.get('a_defects', []))} fid {r.get('a_fidelity')}  B def {len(r.get('b_defects', []))} fid {r.get('b_fidelity')}")
        json.dump(res, open(OUT.replace(".json", "_ref.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        sys.exit(0)
    if mode == "pair":
        da, db = sys.argv[2], sys.argv[3]
        heroes = sorted(f[:-4] for f in os.listdir(da) if f.endswith(".png") and os.path.exists(os.path.join(db, f)))
        def one(h):
            for _ in range(2):
                try: return h, pair(key, os.path.join(da, h + ".png"), os.path.join(db, h + ".png"), h)
                except Exception as e: err = str(e)[:80]
            return h, {"cleaner": "error", "a_defects": [err], "b_defects": []}
        res = {}
        with cf.ThreadPoolExecutor(6) as ex:
            for h, r in ex.map(one, heroes): res[h] = r; print(f"{h:16s} {r.get('cleaner')}  A:{len(r.get('a_defects', []))} B:{len(r.get('b_defects', []))}")
        json.dump(res, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    else:
        da = sys.argv[2]
        heroes = sorted(f[:-4] for f in os.listdir(da) if f.endswith(".png"))
        def one(h):
            for _ in range(2):
                try: return h, scan(key, os.path.join(da, h + ".png"), h)
                except Exception as e: err = str(e)[:80]
            return h, {"defects": [{"where": "-", "what": err, "severity": "error"}]}
        res = {}
        with cf.ThreadPoolExecutor(6) as ex:
            for h, r in ex.map(one, heroes):
                res[h] = r; d = r.get("defects", []) if isinstance(r, dict) else r
                print(f"{h:16s} high {sum(1 for x in d if x.get('severity') == 'high')}  med {sum(1 for x in d if x.get('severity') == 'medium')}")
        json.dump(res, open(OUT.replace(".json", "_scan.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
