# -*- coding: utf-8 -*-
"""
The measured SD body, as C# tables: per-bone cross-section radius r(t, angle) averaged over the
reference models (right side mirrored onto the left), the head/hair spherical radius maps, and
the face feature boxes. Reads the CSVs written by Assets/_Ref/Editor/Profile2.cs (REF_OUT) and
writes Assets/ExcelHeroes/Scripts/World/SdProfile.cs.

Only numbers leave this script — averaged silhouettes of a generic chibi body, not any model's
mesh. Empty bins are filled from their neighbours (angle first, then along the bone).
"""
import csv, os, sys
import numpy as np

REF = os.environ.get("REF_OUT", r"C:\Users\user\AppData\Local\Temp\claude\C--Users-user-excel-heros\9fdcd279-309f-4636-b147-1cff75c33518\scratchpad\ref")
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "ExcelHeroes", "Scripts", "World", "SdProfile.cs")
NT, NA = 12, 16
BONES = ["Pelvis", "Spine", "Spine1", "Spine2", "Neck", "Head", "UpperArm", "Forearm", "Hand", "Thigh", "Calf", "Foot"]


def fill(grid):
    """Fill NaNs: first by angular neighbours (wrapping), then along t, then global median."""
    g = grid.copy()
    for _ in range(NA):
        nan = np.isnan(g)
        if not nan.any(): break
        left = np.roll(g, 1, axis=1); right = np.roll(g, -1, axis=1)
        est = np.nanmean(np.stack([left, right]), axis=0)
        g[nan] = est[nan]
    for _ in range(NT):
        nan = np.isnan(g)
        if not nan.any(): break
        up = np.vstack([g[:1], g[:-1]]); down = np.vstack([g[1:], g[-1:]])
        est = np.nanmean(np.stack([up, down]), axis=0)
        g[nan] = est[nan]
    g[np.isnan(g)] = np.nanmedian(g) if not np.isnan(np.nanmedian(g)) else 0.02
    return g


def main():
    rows = list(csv.DictReader(open(os.path.join(REF, "profile2.csv"))))
    models = sorted({r["model"] for r in rows})
    tables = {}
    for bone in BONES:
        acc = []
        for m in models:
            g = np.full((NT, NA), np.nan)
            for r in rows:
                if r["model"] != m: continue
                b = r["bone"]
                mirror = b.startswith("R ")
                if b.replace("L ", "").replace("R ", "") != bone: continue
                ti = int(round((float(r["t"]) + 0.2) / 1.4 * NT - 0.5))
                ai = int(float(r["a"]) * NA - 0.5)
                if mirror: ai = (NA - 1 - ai) % NA        # mirror the angle across the front axis
                if 0 <= ti < NT and 0 <= ai < NA:
                    g[ti, ai] = float(r["r"]) if np.isnan(g[ti, ai]) else (g[ti, ai] + float(r["r"])) / 2
            if np.isnan(g).all(): continue
            acc.append(g)
        if not acc: continue
        stack = np.stack(acc)
        # median over models is robust to one character's big skirt or coat
        med = np.nanmedian(stack, axis=0)
        tables[bone] = fill(med)
        print(f"  {bone:9s} models {len(acc)}  r mean {np.nanmean(med):.4f}  max {np.nanmax(med):.4f}")

    # head / hair maps
    hrows = list(csv.DictReader(open(os.path.join(REF, "head.csv"))))
    centres = [(float(r["r"]), float(r["hair_r"])) for r in hrows if r["theta"] == "centre"]
    cy = np.mean([c[0] for c in centres]); cz = np.mean([c[1] for c in centres])
    HT, HP = 12, 24
    hacc, racc = [], []
    for m in models:
        hg = np.full((HT, HP), np.nan); rg = np.full((HT, HP), np.nan)
        for r in hrows:
            if r["model"] != m or r["theta"] == "centre": continue
            ti = int(float(r["theta"]) * HT - 0.5); pi = int(float(r["phi"]) * HP - 0.5)
            if float(r["r"]) >= 0: hg[ti, pi] = float(r["r"])
            if float(r["hair_r"]) >= 0: rg[ti, pi] = float(r["hair_r"])
        if not np.isnan(hg).all(): hacc.append(hg)
        if not np.isnan(rg).all(): racc.append(rg)
    headmap = np.nanmedian(np.stack(hacc), axis=0)
    hairmap = np.nanmedian(np.stack(racc), axis=0)
    # fill
    def fill2(g):
        g = g.copy()
        for _ in range(HP):
            nan = np.isnan(g)
            if not nan.any(): break
            est = np.nanmean(np.stack([np.roll(g, 1, 1), np.roll(g, -1, 1), np.vstack([g[:1], g[:-1]]), np.vstack([g[1:], g[-1:]])]), axis=0)
            g[nan] = est[nan]
        g[np.isnan(g)] = np.nanmedian(g)
        return g
    headmap = fill2(headmap); hairmap = fill2(hairmap)
    print(f"  head centre y {cy:.4f} z {cz:.4f}  head r mean {headmap.mean():.4f}  hair r mean {hairmap.mean():.4f}")

    # face boxes
    frows = list(csv.DictReader(open(os.path.join(REF, "face.csv"))))
    parts = {}
    for p in ("eyemouth", "eyebrow", "face"):
        sel = [r for r in frows if r["part"] == p]
        if not sel: continue
        parts[p] = [np.median([float(r[k]) for r in sel]) for k in ("x0", "y0", "x1", "y1", "z0", "z1")]
        print(f"  {p:9s} x {parts[p][0]:.3f}..{parts[p][2]:.3f}  y {parts[p][1]:.3f}..{parts[p][3]:.3f}  z {parts[p][4]:.3f}..{parts[p][5]:.3f}")

    def arr(g):
        return "{ " + ", ".join("{ " + ", ".join(f"{v:.4f}f" for v in row) + " }" for row in g) + " }"

    cs = ["// Generated by tools/ref_tables.py from the reference SD models: averaged silhouettes, not",
          "// any model's geometry. Radii are fractions of total height.",
          "using UnityEngine;", "", "namespace ExcelHeroes.World", "{",
          "    public static class SdProfile", "    {",
          f"        public const int NT = {NT}, NA = {NA};   // t along the bone (-0.2..1.2), angle (0 = front, CCW seen from the bone's tip)",
          f"        public const int HT = {HT}, HP = {HP};   // head map: theta from the top, phi (0 = +x)",
          f"        public static readonly Vector3 HeadCentre = new(0f, {cy:.4f}f, {cz:.4f}f);"]
    for bone, g in tables.items():
        cs.append(f"        public static readonly float[,] {bone} = new float[,] {arr(g)};")
    cs.append(f"        public static readonly float[,] HeadMap = new float[,] {arr(headmap)};")
    cs.append(f"        public static readonly float[,] HairMap = new float[,] {arr(hairmap)};")
    for p, b in parts.items():
        nm = {"eyemouth": "EyeMouth", "eyebrow": "Eyebrow", "face": "Face"}[p]
        cs.append(f"        public static readonly Rect {nm} = Rect.MinMaxRect({b[0]:.4f}f, {b[1]:.4f}f, {b[2]:.4f}f, {b[3]:.4f}f);   // z {b[4]:.3f}..{b[5]:.3f}")
    cs += ["", "        /// <summary>Bilinear lookup in a bone table: t in -0.2..1.2, angle in 0..1 (wraps).</summary>",
           "        public static float R(float[,] table, float t, float a)", "        {",
           "            var ft = Mathf.Clamp((t + 0.2f) / 1.4f * NT - 0.5f, 0f, NT - 1f);",
           "            var fa = Mathf.Repeat(a * NA - 0.5f, NA);",
           "            int t0 = (int)ft, a0 = (int)fa; var t1 = Mathf.Min(t0 + 1, NT - 1); var a1 = (a0 + 1) % NA;",
           "            float kt = ft - t0, ka = fa - a0;",
           "            return Mathf.Lerp(Mathf.Lerp(table[t0, a0], table[t0, a1], ka), Mathf.Lerp(table[t1, a0], table[t1, a1], ka), kt);",
           "        }", "",
           "        /// <summary>Bilinear lookup in a head map: theta 0..1 from the top, phi 0..1.</summary>",
           "        public static float Sph(float[,] map, float theta, float phi)", "        {",
           "            var ft = Mathf.Clamp(theta * HT - 0.5f, 0f, HT - 1f);",
           "            var fp = Mathf.Repeat(phi * HP - 0.5f, HP);",
           "            int t0 = (int)ft, p0 = (int)fp; var t1 = Mathf.Min(t0 + 1, HT - 1); var p1 = (p0 + 1) % HP;",
           "            float kt = ft - t0, kp = fp - p0;",
           "            return Mathf.Lerp(Mathf.Lerp(map[t0, p0], map[t0, p1], kp), Mathf.Lerp(map[t1, p0], map[t1, p1], kp), kt);",
           "        }", "    }", "}", ""]
    with open(OUT, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(cs))
    print(f"-> {OUT}")


if __name__ == "__main__":
    main()
