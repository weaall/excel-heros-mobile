using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The SD figure built ON the sample model (Resources/Art/SDBase/base.fbx): the sample's
    /// mesh and Bip001 skeleton are used as-is and changed piece by piece into each character —
    /// eye/mouth texture, body texture, hair colour, then our own hair and outfit meshes — the
    /// way the user asked: start from the reference and edit, not from scratch.
    ///
    /// Submeshes on the base body: 0 Body, 1 Face, 2 Hair (CH0184). Each gets its own material so
    /// the parts can be re-textured independently (SdRefLook).
    /// </summary>
    public static class SdRef
    {
        public const float Height = 1.2f;
        static GameObject _prefab;
        static bool _missing;

        static readonly (string bip, string rig)[] BoneMap =
        {
            ("Bip001 Pelvis", "body"), ("Bip001 Head", "head"),
            ("Bip001 L UpperArm", "armL"), ("Bip001 R UpperArm", "armR"),
            ("Bip001 L Thigh", "legL"), ("Bip001 R Thigh", "legR"),
        };

        public static bool Available => Prefab != null;

        static GameObject Prefab
        {
            get
            {
                if (_prefab != null || _missing) return _prefab;
                _prefab = Resources.Load<GameObject>("Art/SDBase/base");
                _missing = _prefab == null;
                Debug.Log($"[SdRef] base prefab {(_prefab != null ? "loaded, renderers " + _prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length : "MISSING")}");
                return _prefab;
            }
        }

        public static ChibiRig Build(string heroId, Transform parent, int layer)
        {
            var prefab = Prefab;
            if (prefab == null) return null;
            // a wrapper so the rig's Root is at the feet and rotates about the figure's own axis
            var root = new GameObject("sdref:" + heroId) { layer = layer }.transform;
            root.SetParent(parent, false);
            var go = Object.Instantiate(prefab, root);
            go.name = "model";
            SetLayer(go.transform, layer);

            // only the body renderer (submeshes body / face / hair / eyemouth / eyebrow): the
            // sample's weapon, bag and other props are separate renderers, and are not ours
            // the body is the renderer that carries the face: the one with the most submeshes
            // (Body / Face / Hair …); the props are single-material renderers, whatever their size
            var rends = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = rends.OrderByDescending(r => r.sharedMesh.subMeshCount).ThenByDescending(r => r.sharedMesh.vertexCount).First();
            foreach (var r in rends) if (r != body) r.gameObject.SetActive(false);

            // rescale so the model is Height tall, feet at y = 0, centred; face +Z. Measured on
            // the samples (RefAnalyze): the face is at +Z already, but the FBX root is not at the
            // feet, so the offset is taken from the body's bounds in the wrapper's space.
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            // the world-space bounds already account for the FBX's bone_root rotation (270° X);
            // transforming localBounds by hand mirrored the figure on Y
            body.updateWhenOffscreen = true;
            var wb = body.bounds;
            var lo = root.InverseTransformPoint(wb.min); var hi = root.InverseTransformPoint(wb.max);
            var size = hi - lo;
            var s = Height / Mathf.Max(1e-5f, size.y);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-(lo.x + hi.x) * 0.5f * s, -lo.y * s, -(lo.z + hi.z) * 0.5f * s);
            // centre on the HIPS, not the bounds: a ponytail reaching back would otherwise push
            // the body forward of the root, and everything hung on the root (skirt, shadow) behind it
            var pelvis = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Bip001 Pelvis");
            if (pelvis != null)
            {
                var pl = root.InverseTransformPoint(pelvis.position);
                go.transform.localPosition -= new Vector3(pl.x, 0f, pl.z);
            }
            // the face is on +Z of the FBX already (RefAnalyze: EyeMouth at z > 0) — our forward
            rends = new[] { body };
            var look = SdRefLook.For(heroId);
            // the hair submesh, found by the SAMPLE's material names before they are replaced
            var hairSub = -1;
            for (var i = 0; i < body.sharedMaterials.Length; i++)
                if (body.sharedMaterials[i] && body.sharedMaterials[i].name.ToLowerInvariant().Contains("hair")) hairSub = i;
            body.sharedMesh = SdRefMesh.Plain(body.sharedMesh);
            // a sample's hair for the style (or the spec's choice) replaces the base cap when the library has it
            var lk0 = SdLook.For(heroId);
            var libHair = lk0.HairLib != "" ? (lk0.HairLib == "base" ? null : lk0.HairLib) : SdRefHairLib.Pick(look.Style, heroId);
            var useLib = libHair != null && SdRefHairLib.Has(libHair);
            SdRefHair.Apply(body, useLib ? "none" : look.Style, hairSub, 0, useLib ? 0 : look.Fringe, useLib || look.Ahoge);


            foreach (var r in rends)
            {
                var mesh = r.sharedMesh;
                var mats = new Material[mesh.subMeshCount];
                for (var i = 0; i < mesh.subMeshCount; i++)
                {
                    var name = (i < r.sharedMaterials.Length && r.sharedMaterials[i] ? r.sharedMaterials[i].name : r.name).ToLowerInvariant();
                    mats[i] = look.MaterialFor(name);
                }
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            var rig = new ChibiRig { Root = root, Height = Height, Model3D = true, RefModel = true };
            var all = go.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
            rig.Body = Find("Bip001 Pelvis") ?? root;
            rig.Head = Find("Bip001 Head") ?? rig.Body;
            // the sample's skull is open behind the face (the ponytail covered it); trimmed styles
            // get a scalp in the hair colour, a ball just inside the hair cap, riding the head bone
            if (useLib) SdRefHairLib.Mount(rig, libHair, lk0, layer);
            if (!useLib && look.Style != "long" && look.Style != "ponytail")
            {
                var c = body.transform.TransformPoint(SdRefHair.HeadCentre);
                // mesh z is up and -y is forward; the ball is built in the wrapper's frame (y up, +z forward)
                var rr = SdRefHair.HeadRadii * body.transform.lossyScale.x;
                var sb = new MeshKit.Builder();
                sb.Ellipsoid(Vector3.zero, new Vector3(rr.x, rr.z, rr.y), Color.white, 16);
                var scalp = MeshKit.Part("scalp", rig.Head, sb.Bake("scalp"), look.MaterialFor("hair"), layer);
                // the ball is built in world units; the head bone carries the wrapper's scale
                var ls = rig.Head.lossyScale;
                scalp.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                scalp.transform.position = c;
                scalp.transform.rotation = root.rotation;
                rig.Renderers.Add(scalp.GetComponent<MeshRenderer>());
            }
            rig.ArmL = Find("Bip001 L UpperArm"); rig.ArmR = Find("Bip001 R UpperArm");
            rig.LegL = Find("Bip001 L Thigh"); rig.LegR = Find("Bip001 R Thigh");
            rig.Spine = Find("Bip001 Spine1") ?? Find("Bip001 Spine");
            rig.ForearmL = Find("Bip001 L Forearm"); rig.ForearmR = Find("Bip001 R Forearm");
            rig.CalfL = Find("Bip001 L Calf"); rig.CalfR = Find("Bip001 R Calf");
            rig.HandL = Find("Bip001 L Hand"); rig.HandR = Find("Bip001 R Hand");
            rig.FootL = Find("Bip001 L Foot"); rig.FootR = Find("Bip001 R Foot");
            rig.Pelvis = Find("Bip001 Pelvis");
            rig.ClavL = Find("Bip001 L Clavicle"); rig.ClavR = Find("Bip001 R Clavicle"); rig.Neck = Find("Bip001 Neck");
            rig.FingersL = new[] { "Bip001 L Finger0", "Bip001 L Finger01", "Bip001 L Finger1", "Bip001 L Finger11", "Bip001 L Finger2", "Bip001 L Finger21" }.Select(Find).ToArray();
            rig.FingersR = new[] { "Bip001 R Finger0", "Bip001 R Finger01", "Bip001 R Finger1", "Bip001 R Finger11", "Bip001 R Finger2", "Bip001 R Finger21" }.Select(Find).ToArray();
            if (rig.Pelvis != null) rig.PelvisRest = rig.Pelvis.localPosition;
            foreach (var r in rends) if (r.gameObject.activeSelf) rig.Renderers.Add(r);
            rig.FaceRenderer = body;
            for (var i = 0; i < body.sharedMaterials.Length; i++)
                if (body.sharedMaterials[i] == look.MaterialFor("eyemouth")) rig.EyeSub = i;

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.26f, 0f, 0f), new Vector3(0f, 0f, 0.18f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);
            if (look.Glasses || look.Sunglasses) { var gk = SdLook.For(heroId); SdRefProps.Glasses(rig, body, root, look.Sunglasses, gk.GlassesStyle, gk.GlassesColor, layer); }
            var k = SdLook.For(heroId);
            if (k.Skirt || k.Dress) SdRefProps.Skirt(rig, root, k, layer);
            SdRefProps.HandProp(rig, root, RoleOf(heroId), k, layer);
            if (k.Outfit is "suit" or "coat" or "labcoat" or "dress" or "shirt" or "vest") SdRefProps.Collar(rig, body, root, k, layer);
            if (k.Outfit is "coat" or "labcoat") SdRefProps.CoatTail(rig, root, k, layer);
            if (k.Tie.a > 0f) SdRefProps.Tie(rig, body, root, k.Tie, layer);
            else if (!k.Male && (k.Skirt || k.Dress) && k.Outfit is "shirt" or "vest" or "cardigan" or "suit") SdRefProps.Ribbon(rig, body, root, k.Accent, layer);
            if (k.Pants && k.Outfit is not "hoodie" and not "apron") SdRefProps.Belt(rig, root, k, layer);
            if (k.Cap.a > 0f) SdRefProps.Cap(rig, body, root, k.Cap, DollData.For(heroId == Data.GameData.MainId ? "intern" : heroId).Has("hardhat"), layer);
            if (k.Headset) SdRefProps.Headset(rig, body, root, k.Accent, layer);
            // hair, tag and hem chains trail the motion (the sample's own secondary bones)
            root.gameObject.AddComponent<SdSecondary>().Init(go.transform, look.Style);
            return rig;
        }

        /// <summary>
        /// Hangs the back sheet on the chest bone (Spine1) so it bows, leans and falls with the
        /// body instead of floating at a fixed spot behind the root — the halo-like piece must
        /// never look detached. Called after ChibiBuilder.AddSheet; the world spot is the one the
        /// per-frame code used (side × 0.12, 0.744, −0.14 in the root's frame).
        /// </summary>
        public static void WearSheet(ChibiRig rig, float scale)
        {
            if (rig.Sheet == null || rig.Spine == null) return;
            var root = rig.Root;
            // behind the head and out past one shoulder, turned a little towards that side so the
            // flat plane reads in perspective, as a halo does (the lean itself is in the texture)
            var pos = root.TransformPoint(new Vector3(rig.SheetSide * 0.27f, 0.84f, -0.16f));
            var rot = root.rotation * Quaternion.Euler(0f, 180f + rig.SheetSide * 16f, rig.SheetSide * 4f);
            rig.Sheet.SetParent(rig.Spine, true);
            var ls = rig.Spine.lossyScale;
            rig.Sheet.localScale = new Vector3(scale / ls.x, scale / ls.y, scale / ls.z);
            rig.Sheet.position = pos; rig.Sheet.rotation = rot;
            rig.SheetWorn = true;
        }

        /// <summary>The hero's role (tank / melee / ranged / healer), "ranged" when the defs are not loaded.</summary>
        public static string RoleOf(string heroId)
        {
            try
            {
                var id = heroId == "intern" ? Data.GameData.MainId : heroId;
                return Data.GameData.Hero(id)?.role ?? "ranged";
            }
            catch (System.Exception) { return "ranged"; }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }

    /// <summary>
    /// The sample mesh carries vertex colours whose alpha is a face-shading mask for the
    /// reference's own shader; on ours that alpha reaches the cutout and punched a band out of
    /// the face (the eye line). The figure is built on a copy with the colour stream removed.
    ///
    /// The body sheet is mostly a PALETTE: the body pieces sample swatches on a strip at v ≈ 0.01
    /// (measured, HairDump: skin u .27–.29, the shirt u .24–.25, the shorts u .19–.21, shoes
    /// u .29–.33). Legs and hands share the skin swatch, so the leg vertices (|x| &lt; 0.002 in
    /// mesh units, z 5–39 % of height) are moved to a cell of their own (LegsUV, an unused
    /// corner) that SdRefTex.Body paints per character — trousers, stockings or bare skin.
    /// </summary>
    public static class SdRefMesh
    {
        static readonly Dictionary<Mesh, Mesh> Cache = new();
        public static readonly Vector2 LegsUV = new(0.62f, 0.24f);     // the thighs (knee 0.19 H and up)
        public static readonly Vector2 SockUV = new(0.62f, 0.30f);     // the shins (below the knee)

        public static Mesh Plain(Mesh src)
        {
            if (Cache.TryGetValue(src, out var m) && m != null) return m;
            m = Object.Instantiate(src);
            m.name = src.name + ":plain";
            var v = m.vertices; var uv = m.uv;
            // vertex colours: white everywhere (alpha 1, so the cutout never sees the sample's
            // face mask), and on the hair the anime highlight ring — a band round the crown at
            // 0.90 of the height with a zigzag, lifted by lowering everything else — plus a
            // darker underside on the hanging strands
            var cols = new Color[v.Length];
            for (var i = 0; i < cols.Length; i++) cols[i] = Color.white;
            {
                var zmn = float.MaxValue; var zmx = float.MinValue;
                foreach (var pv in v) { zmn = Mathf.Min(zmn, pv.z); zmx = Mathf.Max(zmx, pv.z); }
                var HH = zmx - zmn;
                int hairSub = -1;
                for (var si = 0; si < m.subMeshCount && hairSub < 0; si++) if (si == 2) hairSub = si;   // CH0184: sub 2 is the hair
                if (hairSub >= 0)
                    foreach (var i in m.GetTriangles(hairSub))
                    {
                        var hgt = (v[i].z - zmn) / HH;
                        var az = Mathf.Atan2(v[i].y - 0.0006f, v[i].x);
                        var ring = Mathf.Exp(-Mathf.Pow((hgt - (0.905f + Mathf.Sin(az * 7f) * 0.012f)) / 0.028f, 2f));
                        var shade = hgt < 0.62f ? 0.9f : 1f;
                        var k = (0.8f + 0.2f * ring) * shade;
                        cols[i] = new Color(k, k, k, 1f);
                    }
            }
            m.colors = cols;
            var zmin = float.MaxValue; var zmax = float.MinValue;
            foreach (var p in v) { zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z); }
            var H = zmax - zmin;
            var moved = 0;
            foreach (var i in m.GetTriangles(0))
            {
                var h = (v[i].z - zmin) / H;
                if (uv[i].x > 0.265f && uv[i].x < 0.29f && uv[i].y < 0.05f && h > 0.05f && h < 0.39f && Mathf.Abs(v[i].x) < 0.002f)
                { uv[i] = h < 0.205f ? SockUV : LegsUV; moved++; }
            }
            m.uv = uv;
            Debug.Log($"[SdRefMesh] legs remapped: {moved} vertex refs");
            return Cache[src] = m;
        }
    }

    /// <summary>
    /// The per-character edits on the base: which texture goes on which part. First pass: the
    /// sample's own textures, tinted per part from the character's sampled colours (SdLook), so
    /// every character is already distinguishable; next passes replace the textures outright
    /// (eye/mouth from FaceTexture, body/hair repainted by Gemini).
    /// </summary>
    public class SdRefLook
    {
        static readonly Dictionary<string, SdRefLook> Cache = new();
        readonly SdLook _k;
        readonly Dictionary<string, Material> _mats = new();

        SdRefLook(SdLook k) { _k = k; }
        public string Style => _k.Style;
        public bool Male => _k.Male;
        public bool Ahoge => _k.Ahoge;
        public bool Glasses => _k.Glasses;
        public bool Sunglasses => _k.Sunglasses;
        /// <summary>Fringe variant by character (stable hash): 0 sample, 1 longer, 2/3 swept, 4 short and parted.</summary>
        public int Fringe
        {
            get
            {
                if (_k.Fringe >= 0) return _k.Fringe;
                if (_k.Style == "spiky") return 4;
                if (_k.Style == "side") return 2;
                var h = 0; foreach (var ch in _k.Id) h = h * 31 + ch;
                return Mathf.Abs(h) % 5;
            }
        }
        /// <summary>The eye/mouth sheet for an expression ("" normal, happy, hurt, angry).</summary>
        public Texture2D EyeSheet(string expr) => SdRefTex.EyeMouth(_k, expr);

        public static SdRefLook For(string heroId)
        {
            if (Cache.TryGetValue(heroId, out var l)) return l;
            return Cache[heroId] = new SdRefLook(SdLook.For(heroId));
        }

        /// <summary>The character's 4-cell sheet as painted on the back (BackSheet/SheetTexture), for the name tag.</summary>
        static Texture2D SheetFor(string heroId)
        {
            try
            {
                var id = heroId == "intern" ? Data.GameData.MainId : heroId;
                var def = Data.GameData.Hero(id);
                if (def == null) return null;
                var owned = Core.Game.Player?.Find(id);
                return SheetTexture.For(UI.BackSheet.For(def, owned), id);
            }
            catch (System.Exception) { return null; }       // the editor preview has no game data loaded
        }

        static Texture2D Tex(string n) => Resources.Load<Texture2D>("Art/SDBase/" + n);

        public Material MaterialFor(string matName)
        {
            var key = matName.Contains("eyemouth") ? "eyemouth" : matName.Contains("eyebrow") ? "eyebrow"
                    : matName.Contains("hair") ? "hair" : matName.Contains("face") ? "face" : "body";
            // the brow quads sample the FACE texture's brow strip (Uv.cs), not the eye sheet
            if (_mats.TryGetValue(key, out var m)) return m;
            switch (key)
            {
                case "hair":
                    // the sample's hair sheet repainted in the character's colour (SdRefTex.Hair)
                    m = MeshKit.NewToon(0.004f, SdRefTex.Hair(_k));
                    m.SetFloat("_ShadeStrength", 0.22f);
                    break;
                case "eyebrow":
                    // the brow strips sample the FACE sheet's brow bars
                    m = MeshKit.NewToon(0f, SdRefTex.Face(_k));
                    m.SetFloat("_Cutoff", 0.5f);
                    m.SetFloat("_OutlineWidth", 0f);
                    m.SetFloat("_ShadeStrength", 0.0f);
                    break;
                case "eyemouth":
                    // shaped eye / mouth plates; our sheet's alpha cuts the eye white, iris and mouth line
                    m = MeshKit.NewToon(0f, SdRefTex.EyeMouth(_k));
                    m.SetFloat("_Cutoff", 0.5f);
                    m.SetFloat("_OutlineWidth", 0f);
                    m.SetFloat("_ShadeStrength", 0.0f);
                    break;
                case "face":
                    // flat-lit like the reference's face shading; the lash plates cut by alpha
                    m = MeshKit.NewToon(0.004f, SdRefTex.Face(_k));
                    m.SetFloat("_Cutoff", 0.5f);
                    m.SetFloat("_ShadeStrength", 0.06f);
                    break;
                default:
                    m = MeshKit.NewToon(0.004f, SdRefTex.Body(_k, SheetFor(_k.Id)));
                    m.SetFloat("_ShadeStrength", 0.22f);
                    break;
            }
            m.SetFloat("_Rim", key is "eyemouth" or "eyebrow" ? 0f : 0.1f);
            // SD_HIDE=key[,key]: clip that part entirely (preview diagnostics)
            var hide = System.Environment.GetEnvironmentVariable("SD_HIDE");
            if (!string.IsNullOrEmpty(hide) && hide.Split(',').Contains(key)) m.SetFloat("_Cutoff", 2f);
            var tint = System.Environment.GetEnvironmentVariable("SD_TINT");
            if (!string.IsNullOrEmpty(tint) && tint.Split(',').Contains(key)) m.SetColor("_Color", Color.magenta);
            var noCut = System.Environment.GetEnvironmentVariable("SD_NOCUT");
            if (!string.IsNullOrEmpty(noCut) && noCut.Split(',').Contains(key)) m.SetFloat("_Cutoff", 0f);
            var noOutline = System.Environment.GetEnvironmentVariable("SD_NOOUTLINE");
            if (!string.IsNullOrEmpty(noOutline) && noOutline.Split(',').Contains(key)) m.SetFloat("_OutlineWidth", 0f);
            return _mats[key] = m;
        }
    }
}
