# -*- coding: utf-8 -*-
"""
The 3D SD's outfit colours read from the character's CURRENT standing illustration (Gemini
vision) and recorded in sdspec as hand edits (sd_spec.py --set, so a later run keeps them):
top (jacket / outer layer), shirt (the layer under it), bottom (skirt / trousers), legs, shoes.
Needed after an illustration is revised — looks.json samples the old 2D SD.

    python tools/sdspec_colors_gemini.py hr_jung cco ...
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, json, os, subprocess, sys, urllib.request
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g

STAND = os.path.join(g.ROOT, "Assets", "ExcelHeroes", "Resources", "Art", "Standing")
ASK = ("A full-body character illustration. Give the dominant colour of each clothing layer as hex. Answer JSON only: "
       '{"top": "#rrggbb" (the outermost upper garment: blazer / jacket / cardigan / sweater / vest, or the shirt if there is none), '
       '"shirt": "#rrggbb" (the layer under it, or the same as top), "bottom": "#rrggbb" (skirt or trousers), '
       '"legs": "#rrggbb" (tights / stockings, or skin if bare), "shoes": "#rrggbb"}')


def ask(key, path):
    from PIL import Image
    import io
    im = Image.open(path).convert("RGBA"); bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im)
    buf = io.BytesIO(); bg.convert("RGB").save(buf, "PNG")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode("ascii")}}, {"text": ASK}]}],
                       "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode("utf-8")
    req = urllib.request.Request("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.8-flash:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    with urllib.request.urlopen(req, timeout=180) as r: d = json.loads(r.read().decode("utf-8"))
    t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
    r = json.loads(t); return r[0] if isinstance(r, list) else r


if __name__ == "__main__":
    key = g.read_key()
    for hid in sys.argv[1:]:
        c = ask(key, os.path.join(STAND, hid + ".png"))
        kv = [f"{k}={c[k]}" for k in ("top", "shirt", "bottom", "legs", "shoes") if isinstance(c.get(k), str) and c[k].startswith("#")]
        subprocess.run([sys.executable, os.path.join(os.path.dirname(os.path.abspath(__file__)), "sd_spec.py"), "--set", hid, *kv], check=False)
        print(hid, " ".join(kv))
