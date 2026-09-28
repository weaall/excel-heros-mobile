# -*- coding: utf-8 -*-
"""
The TRELLIS retry pass: a reconstruction whose silhouette matches the drawing badly (IoU < 0.8 in
tools/out/mon3d_pack/iou.json, e.g. the flat boss that came back as a black card) is generated again
with seeds 1..3, each packed and scored; the best one is kept.   python tools/mon3d_fix.py [ids]

When seeds do not help (the flat, flame-framed boss is read as a card every time), Gemini renders
the same mascot as a chunky 3D vinyl toy on white, and TRELLIS reconstructs THAT; the pack still
projects the original drawing onto the front, so the face is the drawing's.
"""
import json, os, shutil, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mon3d_trellis as tr
IOU = os.path.join(HERE, "out", "mon3d_pack", "iou.json")
GOOD = 0.8

def score(i):
    subprocess.run([sys.executable, os.path.join(HERE, "mon3d_pack.py"), i], check=True, stdout=subprocess.DEVNULL)
    return json.load(open(IOU)).get(i, 0.0)

TOY = ("Render this exact mascot as a 3D vinyl toy figure: the same character, pose, colours, face and parts, but "
       "solid chunky 3D volumes with soft studio lighting (like a Blue Archive 3D SD model or a Funko/Nendoroid-style toy). "
       "Anything that surrounds the body (flames, aura, frames) becomes a few chunky 3D shapes attached to its back and sides, "
       "not a flat backdrop. Full body, 3/4 view facing left, centred, on a plain pure white background, no shadow, no text.")

def toy_render(i):
    import gen_cards_gemini as g, gen_monsters_v2 as mv, gen_icons_gemini as ic
    from PIL import Image
    raw = os.path.join(HERE, "out", "mon3d_toy", i + ".png"); os.makedirs(os.path.dirname(raw), exist_ok=True)
    if not mv.generate(g.read_key(), os.path.join(tr.SRC, i + ".png"), TOY, raw): return None
    cut = raw[:-4] + "_cut.png"
    ic.cut_white(Image.open(raw).convert("RGB")).save(cut)
    return cut

if __name__ == "__main__":
    scores = json.load(open(IOU)) if os.path.exists(IOU) else {}
    ids = [a for a in sys.argv[1:] if not a.startswith("--")] or sorted(k for k, v in scores.items() if v < GOOD)
    pools = tr.Pools()
    for i in ids:
        glb = os.path.join(tr.OUT, i + ".glb")
        best = (scores.get(i, 0.0), 0); shutil.copy(glb, glb + ".s0")
        for seed in ((1, 2, 3) if "--seeds" in sys.argv else ()):
            if best[0] >= GOOD: break
            if not tr.generate(pools, os.path.join(tr.SRC, i + ".png"), glb, seed=seed): continue
            s = score(i); shutil.copy(glb, glb + f".s{seed}")
            print(f"  {i} seed {seed}: IoU {s:.2f}")
            if s > best[0]: best = (s, seed)
        if best[0] < GOOD:
            toy = toy_render(i) if not os.path.exists(os.path.join(HERE, "out", "mon3d_toy", i + "_cut.png")) else os.path.join(HERE, "out", "mon3d_toy", i + "_cut.png")
            if toy and tr.generate(pools, toy, glb, seed=0):
                s = score(i); shutil.copy(glb, glb + ".toy")
                print(f"  {i} toy render: IoU {s:.2f}")
                if s > best[0]: best = (s, "toy")
        shutil.copy(glb + (".toy" if best[1] == "toy" else f".s{best[1]}"), glb)
        final = score(i)
        print(f"  {i}: kept seed {best[1]} (IoU {final:.2f})")
