# -*- coding: utf-8 -*-
"""
Enemies v4 — a real 3D head per enemy, not an inflated drawing: Gemini's vision model reads each
mascot (Resources/Art/SDMonsters/<id>.png) as a recipe of 3D primitives that World/SdEnemyHead
builds — a base shape with its proportions and roundness, colour bands and a front panel, the
object's parts (ears, horns, handle, tray, antenna, crown…) and a face of 3D parts (eyeballs with
iris and highlight, brows, a mouth, fangs, cheeks) → Resources/Data/enemyhead.json.

    python tools/gen_enemyrecipe_gemini.py            # every mascot missing one
    python tools/gen_enemyrecipe_gemini.py copier --force
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, io, json, os, re, sys, time, urllib.request
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

SRC = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "SDMonsters")
OUT = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Data", "enemyhead.json")
MODEL = os.environ.get("QA_MODEL", "gemini-3.8-flash")
SHAPES = ["box", "sphere", "capsule", "cylinder", "drop", "cone", "egg", "blob"]
PARTS = ["ear_round", "ear_pointed", "ear_long", "horn", "antenna", "crown", "handle", "tray", "slot", "button", "screen",
         "spike", "leaf", "flame", "tuft", "wing", "cap", "bow", "chain", "lid", "paper", "lens", "beak", "snout", "tongue", "tail"]
ASK = ("This is an enemy mascot of our game (office errors and office objects come to life). We rebuild its HEAD as a real 3D toy "
       "from primitives (the rest of the body is built separately — describe only the part that is its head/main object, "
       "with the face on it). Answer JSON only:\n"
       '{"shape": one of ' + json.dumps(SHAPES) + ', "size": [width, height, depth] relative (width 1), "round": 0.15 (nearly square '
       'edges) .. 1 (fully round), "color": "#rrggbb" main, "color2": "#rrggbb" second, "dark": "#rrggbb" darkest, '
       '"bands": [{"y": 0..1 from the bottom, "h": 0..1, "color": "#rrggbb"}] (up to 3 horizontal stripes/panels on the shape), '
       '"panel": null or {"y": 0..1, "w": 0..1, "h": 0..1, "color": "#rrggbb"} (a front panel/screen the face sits on), '
       '"parts": [{"kind": one of ' + json.dumps(PARTS) + ', "at": "top"|"left"|"right"|"both"|"front"|"back", "color": "#rrggbb", "size": 0.1..0.6}] (up to 5), '
       '"eyes": {"style": "round"|"angry"|"sleepy"|"sharp"|"happy"|"cyclops", "iris": "#rrggbb", "size": 0.1..0.35, "y": 0.3..0.8, "gap": 0.15..0.5}, '
       '"brows": true|false, "mouth": "smile"|"grin"|"frown"|"fangs"|"open"|"wavy"|"teeth"|"none", "mouth_y": 0.1..0.5, "cheeks": true|false}\n'
       "Be faithful to the picture: its silhouette, where the face is, its colours and its signature parts.")


def ask(key, path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGB", im.size, (255, 255, 255)); bg.paste(im, (0, 0), im)
    buf = io.BytesIO(); bg.save(buf, "PNG")
    parts = [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": ASK}]
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": {"responseMimeType": "application/json", "temperature": 0.2}}).encode("utf-8")
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
                                 data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            with urllib.request.urlopen(req, timeout=120) as r: return json.loads(json.loads(r.read().decode("utf-8"))["candidates"][0]["content"]["parts"][0]["text"])
        except (OSError, KeyError, json.JSONDecodeError): time.sleep(3)
    return None


def clean(a):
    hexok = lambda c, fb: c if isinstance(c, str) and re.fullmatch(r"#[0-9a-fA-F]{6}", c) else fb
    num = lambda v, lo, hi, fb: max(lo, min(hi, float(v))) if isinstance(v, (int, float)) else fb
    size = a.get("size") if isinstance(a.get("size"), list) and len(a.get("size")) == 3 else [1, 1, 0.9]
    r = {"shape": a.get("shape") if a.get("shape") in SHAPES else "blob",
         "sx": num(size[0], 0.4, 2, 1), "sy": num(size[1], 0.4, 2, 1), "sz": num(size[2], 0.3, 2, 0.9),
         "round": num(a.get("round"), 0.12, 1, 0.5),
         "color": hexok(a.get("color"), "#cccccc"), "color2": hexok(a.get("color2"), "#eeeeee"), "dark": hexok(a.get("dark"), "#333344"),
         "bands": [], "panel": None, "parts": []}
    for b in (a.get("bands") or [])[:3]:
        if isinstance(b, dict): r["bands"].append({"y": num(b.get("y"), 0, 1, 0.5), "h": num(b.get("h"), 0.02, 1, 0.1), "color": hexok(b.get("color"), r["color2"])})
    p = a.get("panel")
    if isinstance(p, dict): r["panel"] = {"y": num(p.get("y"), 0, 1, 0.5), "w": num(p.get("w"), 0.1, 1, 0.7), "h": num(p.get("h"), 0.1, 1, 0.5), "color": hexok(p.get("color"), r["color2"])}
    for q in (a.get("parts") or [])[:5]:
        if isinstance(q, dict) and q.get("kind") in PARTS:
            r["parts"].append({"kind": q["kind"], "at": q.get("at") if q.get("at") in ("top", "left", "right", "both", "front", "back") else "top",
                               "color": hexok(q.get("color"), r["color2"]), "size": num(q.get("size"), 0.05, 0.8, 0.25)})
    e = a.get("eyes") if isinstance(a.get("eyes"), dict) else {}
    r["eyes"] = {"style": e.get("style") if e.get("style") in ("round", "angry", "sleepy", "sharp", "happy", "cyclops") else "angry",
                 "iris": hexok(e.get("iris"), "#2a2a3a"), "size": num(e.get("size"), 0.08, 0.4, 0.2), "y": num(e.get("y"), 0.2, 0.85, 0.55),
                 "gap": num(e.get("gap"), 0.1, 0.6, 0.3)}
    r["brows"] = bool(a.get("brows", True))
    r["mouth"] = a.get("mouth") if a.get("mouth") in ("smile", "grin", "frown", "fangs", "open", "wavy", "teeth", "none") else "frown"
    r["mouth_y"] = num(a.get("mouth_y"), 0.05, 0.6, 0.3)
    r["cheeks"] = bool(a.get("cheeks", False))
    return r


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]; force = "--force" in sys.argv
    table = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {"items": []}
    rows = {r["id"]: r for r in table["items"]}
    key = g.read_key()
    for i in args or sorted(p[:-4] for p in os.listdir(SRC) if p.endswith(".png")):
        if i in rows and not force: continue
        a = ask(key, os.path.join(SRC, i + ".png"))
        if not a: print("  ?", i); continue
        r = clean(a); r["id"] = i; rows[i] = r
        print(f"  {i:14s} {r['shape']:8s} {len(r['parts'])} parts  eyes {r['eyes']['style']}  mouth {r['mouth']}")
        table["items"] = sorted(rows.values(), key=lambda x: x["id"])
        json.dump(table, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
