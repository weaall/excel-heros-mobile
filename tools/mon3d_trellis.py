# -*- coding: utf-8 -*-
"""
3D monsters v2: each v2 mascot (Resources/Art/SDMonsters/<id>.png, tools/gen_monsters_v2.py)
reconstructed by TRELLIS (Microsoft, MIT; the trellis-community/TRELLIS Space on Hugging Face) into
a textured GLB. TripoSR made lumpy vertex-coloured shapes from the first, inconsistent sprites; the
new mascots are round, outlined and one style, which is what image-to-3D reconstructs well, and
TRELLIS gives a real texture.

    python tools/mon3d_trellis.py circ copier     # some
    python tools/mon3d_trellis.py                 # every mascot without a GLB yet
    python tools/mon3d_trellis.py --force circ
      → tools/out/mon3d_glb/<id>.glb (raw)   then   python tools/mon3d_pack.py

ZeroGPU quota: the six HF tokens are rotated, then anonymous (7 pools). SECURITY: tokens are read
from the env file BY NAME (HUGGING_FACE_API_KEY*) into memory only; never printed or written.
"""
import contextlib, io, os, re, shutil, sys, time

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "..", "Assets", "ExcelHeroes", "Resources", "Art", "SDMonsters")
OUT = os.path.join(HERE, "out", "mon3d_glb")
ENV_FILES = [os.environ.get("ENV_FILE", ""), r"C:\Users\minds\Desktop\mindsai_weaall.env", r"C:\Users\user\Desktop\mindsai_weaall.env"]
SPACE = "trellis-community/TRELLIS"


def tokens():
    for p in ENV_FILES:
        if p and os.path.exists(p):
            out = []
            for line in open(p, encoding="utf-8", errors="ignore"):
                m = re.match(r"\s*(HUGGING_FACE_API_KEY\w*)\s*=\s*(.+)", line)
                if m: out.append(m.group(2).strip().strip('"').strip("'"))
            return out
    return []


class Pools:
    """Token 0..5, then None (anonymous). A pool that hits its GPU quota is skipped from then on."""
    def __init__(self):
        self.list = tokens() + [None]; self.i = 0; self.client = None

    def get(self):
        from gradio_client import Client
        while self.client is None:
            if self.i >= len(self.list): raise RuntimeError("all pools exhausted")
            try:
                with contextlib.redirect_stdout(io.StringIO()):
                    self.client = Client(SPACE, token=self.list[self.i], verbose=False)
            except Exception as e:
                print(f"    pool {self.i}: connect failed ({type(e).__name__})"); self.i += 1
        return self.client

    def next(self, why):
        print(f"    pool {self.i}: {why} → next"); self.i += 1; self.client = None


def generate(pools, png, out, seed=0):
    from gradio_client import handle_file
    for _ in range(12):
        c = pools.get()
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                try: c.predict(api_name="/start_session")
                except Exception: pass
                res = c.predict(image=handle_file(png), multiimages=[], seed=seed,
                                ss_guidance_strength=7.5, ss_sampling_steps=12,
                                slat_guidance_strength=3.0, slat_sampling_steps=12,
                                multiimage_algo="stochastic", mesh_simplify=0.95, texture_size=1024,
                                api_name="/generate_and_extract_glb")
            glb = res[1] if isinstance(res, (list, tuple)) else res
            if isinstance(glb, dict): glb = glb.get("value") or glb.get("path")
            shutil.copy(glb, out)
            return True
        except Exception as e:
            msg = str(e)
            if re.search(r"quota|ZeroGPU|exceeded|GPU task aborted|429|rate", msg, re.I): pools.next("quota")
            else:
                print(f"    error: {type(e).__name__}: {msg[:160]}"); pools.next("error"); time.sleep(3)
    return False


if __name__ == "__main__":
    args = sys.argv[1:]; force = "--force" in args
    ids = [a for a in args if not a.startswith("--")] or sorted(f[:-4] for f in os.listdir(SRC) if f.endswith(".png"))
    os.makedirs(OUT, exist_ok=True)
    pools = Pools(); ok = 0
    for i in ids:
        out = os.path.join(OUT, i + ".glb")
        if os.path.exists(out) and not force: print(f"  skip {i}"); ok += 1; continue
        t = time.time()
        if generate(pools, os.path.join(SRC, i + ".png"), out): print(f"  ok   {i} ({time.time() - t:.0f}s, pool {pools.i})"); ok += 1
        else: print(f"  FAIL {i}")
    print(f"done {ok}/{len(ids)}")
