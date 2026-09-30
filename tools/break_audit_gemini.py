# -*- coding: utf-8 -*-
"""
Model-breakage audit on the motion strips (the user: "the models break a bit in battle"). Each sheet is ten
characters side by side (tools/out/break_sheet_<n>.png), every character a column of four rows — attack,
EX skill, hit, down — three frames each. Gemini lists, per character, only real rendering DEFECTS: torn or
stretched mesh, spikes, floating or detached pieces, limbs passing through the body or head, hair through the
face, missing parts, flipped normals / holes. JSON → tools/out/break_audit.json; the worst printed first.

    python tools/break_audit_gemini.py
SECURITY: the key is read from the env file by NAME; never printed or written.
"""
import base64, glob, io, json, os, sys, urllib.request, concurrent.futures as cf
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_cards_gemini as g
from PIL import Image

OUT = os.path.join(g.ROOT, "tools", "out")
MODEL = os.environ.get("QA_MODEL", "gemini-3.1-pro-preview")
ASK = ("This sheet shows {n} chibi 3D game characters side by side, left to right: {ids}. Each character is one column of "
       "4 rows (attack, special skill, getting hit, knocked down), 3 animation frames per row. Find only real 3D RENDERING "
       "DEFECTS: torn or stretched mesh, spikes, floating or detached pieces, a hand/arm/leg passing through the body or head, "
       "hair or accessories passing through the face, missing body parts, holes. Ignore art style, pose choice, and motion blur-"
       "like hair swing. Be strict but do not invent problems. JSON only: "
       '[{{"id": "...", "severity": 0-3, "defects": ["row: what and where"]}}] — one entry per character, severity 0 when clean.')


def call(key, path, ids):
    im = Image.open(path).convert("RGB")
    if im.width > 3800: im = im.resize((3800, round(3800 * im.height / im.width)), Image.LANCZOS)
    buf = io.BytesIO(); im.save(buf, "PNG")
    body = json.dumps({"contents": [{"parts": [{"inlineData": {"mimeType": "image/png", "data": base64.b64encode(buf.getvalue()).decode()}},
                                               {"text": ASK.format(n=len(ids), ids=", ".join(ids))}]}],
                       "generationConfig": {"temperature": 0.1, "responseMimeType": "application/json"}}).encode()
    req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "x-goog-api-key": key})
    for _ in range(3):
        try:
            d = json.loads(urllib.request.urlopen(req, timeout=400).read())
            t = "".join(p.get("text", "") for c in d.get("candidates", []) for p in c.get("content", {}).get("parts", []) if not p.get("thought"))
            return json.loads(t)
        except Exception as e: err = e
    print("  failed", path, err); return []


if __name__ == "__main__":
    key = g.read_key()
    strips = sorted(f[6:-4] for f in os.listdir(os.path.join(OUT, "break")) if f.startswith("strip_"))
    jobs = [(os.path.join(OUT, f"break_sheet_{k // 10}.png"), strips[k:k + 10]) for k in range(0, len(strips), 10)]
    with cf.ThreadPoolExecutor(4) as ex: res = [r for rs in ex.map(lambda j: call(key, *j), jobs) for r in rs]
    json.dump(res, open(os.path.join(OUT, "break_audit.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    for r in sorted(res, key=lambda r: -int(r.get("severity", 0))):
        if int(r.get("severity", 0)) > 0: print(f"{r['id']:16s} {r['severity']} | " + " ; ".join(r.get("defects", [])))
    print("clean:", sum(1 for r in res if int(r.get("severity", 0)) == 0), "/", len(res))
