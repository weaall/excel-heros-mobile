# -*- coding: utf-8 -*-
"""
The 3D SD's identity read from the character's CURRENT standing illustration (Gemini vision):
hair colour, eye colour, glasses / sunglasses, hairstyle. The old values came from the old 2D SD
(looks.json) and several no longer matched the illustration (a pink-haired designer with navy
hair in 3D). Prints the differences; --apply records them in sdspec (sd_spec.py --set).

    python tools/sdspec_identity_gemini.py                    # every hero, report only
    python tools/sdspec_identity_gemini.py --apply cco chro   # record
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, colorsys, io, json, os, subprocess, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
ASK = ("A character illustration. Answer JSON only: {\"hair\": \"#rrggbb\" (the dominant mid-tone of the hair, not the highlight or the "
       "shadow), \"eye\": \"#rrggbb\" (the iris colour), \"glasses\": true|false (clear eyeglasses), \"sunglasses\": true|false, "
       "\"style\": one of \"short\", \"bob\", \"long\", \"ponytail\", \"twin\", \"side\", \"curly\", \"bun\", \"spiky\"}")


def ask(key, path):
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    bg = bg.convert("RGB"); bg.thumbnail((768, 1400)); buf = io.BytesIO(); bg.save(buf, "PNG")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": ASK}]}],
                       "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=180) as r: d = json.loads(r.read().decode("utf-8"))
    t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    r = json.loads(t); return r[0] if isinstance(r, list) else r


def dist(a, b):
    """Perceptual-ish distance of two hex colours (0 = same, ~1 = unrelated)."""
    try:
        ca = [int(a.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4)]; cb = [int(b.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    except Exception: return 1.0
    ha, la, sa = colorsys.rgb_to_hls(*ca); hb, lb, sb = colorsys.rgb_to_hls(*cb)
    dh = min(abs(ha - hb), 1 - abs(ha - hb)) * 2 * min(sa, sb)
    return max(dh, abs(la - lb) * 1.2, abs(sa - sb) * 0.6)


if __name__ == "__main__":
    key = g.read_key()
    spec = json.load(open(os.path.join(g.DATA, "sdspec.json"), encoding="utf-8"))
    it = spec.get("items", spec); rows = {r["id"]: r for r in (it if isinstance(it, list) else [dict(v, id=k) for k, v in it.items()])}
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or [h["id"] for h in json.load(open(os.path.join(g.DATA, "heroes.json"), encoding="utf-8"))["items"]]
    import concurrent.futures as cf
    with cf.ThreadPoolExecutor(6) as ex: res = dict(zip(ids, ex.map(lambda h: ask(key, os.path.join(STAND, h + ".png")), ids)))
    json.dump(res, open(os.path.join(g.ROOT, "tools", "out", "identity.json"), "w"), indent=1)
    for h, c in res.items():
        r = rows[h]; kv = []
        if dist(r.get("hair", ""), c["hair"]) > 0.22: kv.append(f"hair={c['hair']}")
        if dist(r.get("eye", ""), c["eye"]) > 0.3: kv.append(f"eye={c['eye']}")
        if bool(r.get("glasses")) != bool(c.get("glasses")): kv.append(f"glasses={'true' if c.get('glasses') else 'false'}")
        if bool(r.get("sunglasses")) != bool(c.get("sunglasses")): kv.append(f"sunglasses={'true' if c.get('sunglasses') else 'false'}")
        note = f"   (style {r.get('style')} → {c.get('style')})" if c.get("style") != r.get("style") else ""
        if kv or note: print(h, " ".join(kv), f"[was hair {r.get('hair')} eye {r.get('eye')}]", note)
        if kv and "--apply" in sys.argv:
            subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "sd_spec.py"), "--set", h, *kv], check=False, capture_output=True)
