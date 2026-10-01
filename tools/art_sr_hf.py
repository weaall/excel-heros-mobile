# -*- coding: utf-8 -*-
"""
Anime super-resolution pass over the painted art, on a Hugging Face Space (Phips/Upscaler, ZeroGPU): the
2xHFA2k models are trained on anime illustrations, so the line art comes back cleaner and the generator's
grain on flat cel tones goes away, without redrawing anything (the user, 2026-10-01: "use Hugging Face to
polish the start illustrations, backgrounds and elements"). A generative polish (FLUX.1-Kontext) was tried
on the prologue first and rejected: it flattened the monitor glow and changed the intern's face.

Per group, what comes back:
  story     prologue CGs: 2x, then back to 2544x1080 (supersampled clean; the screen is 1080 tall)
  battle    painted far backdrops 1344x768: 2x → 2688x1536 (the importer fits it to 2048)
  plate     painted street plates 2048x1152: 2x → 4096x2304 (importer cap raised to 4096 for plate_)
  backdrop  the lobby 2048x869: 2x → 3072x1304 (fills a 2400-wide screen; DXT needs multiples of 4). Not menu:
            that one is blurred and hazed on purpose behind the menus, and the SR sharpened the blur back
  notice    event banners 1344x570: 2x → 2048x868
  loading   gag panels 1024x765: 2x → 2048x1528
  standing  the 56 illustrations 768x1344 RGBA: 2x, then back to 768x1344, alpha kept (clean only;
            the edge pixels keep their own colour so nothing halos)
  cards     the 198 card arts 512x748: 2x, back to size (clean)

    python tools/art_sr_hf.py story battle ...     [--model 2xHFA2kOmniSR] [--force]
Originals are copied to ArtSource/SR_backup/<group>/ once (never overwritten); a file already done is
skipped unless --force (the list is kept in ArtSource/SR_backup/done.txt).
SECURITY: the tokens are read from the env file BY NAME (HUGGING_FACE_API_KEY*) into memory only.
"""
import glob, os, shutil, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mon3d_trellis import tokens
from PIL import Image, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
ART = os.path.join(ROOT, "Assets", "ExcelHeroes", "Resources", "Art")
BAK = os.path.join(ROOT, "ArtSource", "SR_backup")
DONE = os.path.join(BAK, "done.txt")
SPACE = "Phips/Upscaler"

# group -> (glob under Art/, final size: "orig" | (w, h))
GROUPS = {
    "story": ("Story/*.png", "orig"),
    "battle": ("Battle/[os]*_*.png", (2688, 1536)),
    "plate": ("Battle/plate_*.png", (4096, 2304)),
    "backdrop": ("Backdrop/lobby.png", (3072, 1304)),   # not menu: it is blurred on purpose so the UI reads
    "notice": ("Notice/*.png", (2048, 868)),
    "loading": ("Loading/*.png", (2048, 1528)),
    "standing": ("Standing/*.png", "orig"),
    "cards": ("Cards/*.png", "orig"),
}


class Pools:
    def __init__(self):
        self.list = tokens() + [None]; self.dead = set()

    def upscale(self, src, model):
        from gradio_client import Client, handle_file
        for i, t in enumerate(self.list):
            if i in self.dead: continue
            try:
                c = Client(SPACE, token=t, verbose=False) if t else Client(SPACE, verbose=False)
                r = c.predict(image=handle_file(src), model_selection=model, api_name="/upscale_image")
                p = r[1] if isinstance(r, (list, tuple)) else r
                if isinstance(p, dict): p = p.get("path")
                return p, i
            except Exception as e:
                m = str(e)
                if "quota" in m.lower() or "exceeded" in m.lower() or "gpu" in m.lower(): self.dead.add(i); print(f"    pool {i} out of quota"); continue
                print(f"    pool {i}: {m[:120]}"); self.dead.add(i)
        return None, -1


def finish(orig_path, sr_path, size, dst):
    o = Image.open(orig_path); s = Image.open(sr_path).convert("RGB")
    alpha = o.getchannel("A") if o.mode in ("RGBA", "LA") else None
    w, h = o.size if size == "orig" else size
    out = s.resize((w, h), Image.LANCZOS)
    if alpha is not None:
        a = alpha.resize((w, h), Image.LANCZOS)
        # the SR saw the art on white: inside the figure take it, on the soft edge keep the original colours
        solid = a.point(lambda v: 255 if v >= 250 else 0).filter(ImageFilter.MinFilter(5)).filter(ImageFilter.GaussianBlur(1.5))
        base = o.convert("RGB").resize((w, h), Image.LANCZOS)
        out = Image.composite(out, base, solid); out.putalpha(a)
    out.save(dst)


def flat_input(path, tmp):
    im = Image.open(path)
    if im.mode == "RGBA":
        bg = Image.new("RGBA", im.size, (255, 255, 255, 255)); bg.alpha_composite(im); im = bg
    im.convert("RGB").save(tmp); return tmp


def run(groups, model, force):
    os.makedirs(BAK, exist_ok=True)
    done = set(open(DONE, encoding="utf-8").read().split()) if os.path.exists(DONE) else set()
    pools = Pools(); tmp = os.path.join(BAK, "_in.png")
    for gname in groups:
        pat, size = GROUPS[gname]
        bdir = os.path.join(BAK, gname); os.makedirs(bdir, exist_ok=True)
        for p in sorted(glob.glob(os.path.join(ART, pat))):
            key = f"{gname}/{os.path.basename(p)}"
            if key in done and not force: continue
            orig = os.path.join(bdir, os.path.basename(p))
            if not os.path.exists(orig): shutil.copy(p, orig)
            t0 = time.time()
            sr, pool = pools.upscale(flat_input(orig, tmp), model)
            if sr is None: print("  ALL POOLS OUT — stopping at", key); return False
            finish(orig, sr, size, p)
            done.add(key); open(DONE, "w", encoding="utf-8").write("\n".join(sorted(done)))
            print(f"  ok {key} pool {pool} {time.time() - t0:.0f}s")
    return True


if __name__ == "__main__":
    a = sys.argv[1:]
    model = "2xHFA2kOmniSR"
    if "--model" in a: i = a.index("--model"); model = a[i + 1]; del a[i:i + 2]
    force = "--force" in a; a = [x for x in a if not x.startswith("--")]
    run(a or ["story", "battle", "plate", "backdrop", "notice", "loading", "standing"], model, force)
