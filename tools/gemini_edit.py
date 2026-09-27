# -*- coding: utf-8 -*-
"""
Design mock-ups from Gemini (Nano Banana): one or more of our own captures go in with a prompt,
and N redrawn variants come back. The general form of gen_hudref_gemini.py — these are pictures
to design against, never assets the game ships.

    python tools/gemini_edit.py --name halo --prompt-file p.txt [--n 3] [--ratio 21:9] cap1.png [cap2.png ...]
      → tools/out/design/<name>_<i>.png

SECURITY: the key is read from the env file by NAME and sent in a header. It is never printed,
logged or written anywhere else.
"""
import argparse, base64, json, os, sys, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
ENV_FILES = [os.environ.get("ENV_FILE", ""), r"C:\Users\minds\Desktop\mindsai_weaall.env", r"C:\Users\user\Desktop\mindsai_weaall.env"]
MODELS = ["gemini-3-pro-image-preview", "gemini-2.5-flash-image"]


def read_key():
    for path in ENV_FILES:
        if path and os.path.exists(path):
            with open(path, encoding="utf-8") as f:
                for line in f:
                    line = line.strip()
                    if line.startswith("GEMINI_API_KEY="):
                        return line.split("=", 1)[1].strip().strip('"').strip("'")
    sys.exit("GEMINI_API_KEY not found (set ENV_FILE)")


def generate(key, images, prompt, ratio, out):
    parts = []
    for p in images:
        with open(p, "rb") as f:
            parts.append({"inlineData": {"mimeType": "image/png", "data": base64.b64encode(f.read()).decode("ascii")}})
    parts.append({"text": prompt})
    cfg = {"responseModalities": ["IMAGE"], "temperature": 0.9}
    if ratio: cfg["imageConfig"] = {"aspectRatio": ratio}
    body = json.dumps({"contents": [{"parts": parts}], "generationConfig": cfg}).encode("utf-8")
    for model in MODELS:
        req = urllib.request.Request(f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent",
                                     data=body, method="POST", headers={"Content-Type": "application/json", "x-goog-api-key": key})
        try:
            with urllib.request.urlopen(req, timeout=300) as r:
                data = json.loads(r.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            print(f"  {model}: HTTP {e.code} {e.read().decode('utf-8', 'replace')[:300]}"); continue
        for cand in data.get("candidates", []):
            for part in cand.get("content", {}).get("parts", []):
                inline = part.get("inlineData") or part.get("inline_data")
                if inline and inline.get("data"):
                    with open(out, "wb") as f: f.write(base64.b64decode(inline["data"]))
                    print(f"  saved with {model} -> {out}"); return True
        print(f"  {model}: no image ({json.dumps(data)[:200]})")
    return False


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    ap.add_argument("--name", required=True); ap.add_argument("--prompt-file", required=True)
    ap.add_argument("--n", type=int, default=3); ap.add_argument("--ratio", default="")
    ap.add_argument("images", nargs="*")
    a = ap.parse_args()
    with open(a.prompt_file, encoding="utf-8") as f: prompt = f.read()
    outdir = os.path.join(HERE, "out", "design"); os.makedirs(outdir, exist_ok=True)
    key = read_key()
    ok = sum(generate(key, a.images, prompt, a.ratio, os.path.join(outdir, f"{a.name}_{i}.png")) for i in range(a.n))
    print(f"{ok}/{a.n} {a.name}")
