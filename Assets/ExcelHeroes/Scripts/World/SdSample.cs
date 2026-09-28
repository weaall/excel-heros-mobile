using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// A hero wearing one of the sample bodies as it was made (Resources/Art/SDBase/bodies/&lt;key&gt;,
    /// git-ignored, mirrored on Drive): its own outfit, hair and face, the face layered the way BA
    /// layers it (SdFace), the hair and the iris recoloured to the hero, the body sheet recoloured
    /// by its colour clusters (SdSampleTex). Everything that hangs on the Bip001 skeleton — the
    /// clips (SdClips), the poses, the hand prop, the glasses, the secondary chains — works as on
    /// the base, which is the point: the base was a gym outfit under every hero.
    /// </summary>
    public static class SdSample
    {
        static readonly Dictionary<string, GameObject> Prefabs = new();

        public static bool Has(string key) => !string.IsNullOrEmpty(key) && Prefab(key) != null;

        static GameObject Prefab(string key)
        {
            if (Prefabs.TryGetValue(key, out var p)) return p;
            p = Resources.LoadAll<GameObject>("Art/SDBase/bodies/" + key).FirstOrDefault();
            return Prefabs[key] = p;
        }

        static Texture2D Sheet(string key, string part)
        {
            var all = Resources.LoadAll<Texture2D>("Art/SDBase/bodies/" + key);
            return all.FirstOrDefault(t => t.name.ToLowerInvariant().EndsWith("_" + part) || t.name.ToLowerInvariant().EndsWith("_" + part + "_2"))
                ?? all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains(part));
        }

        public static ChibiRig Build(string heroId, string key, Transform parent, int layer)
        {
            var prefab = Prefab(key);
            if (prefab == null) return null;
            var root = new GameObject("sdsample:" + heroId) { layer = layer }.transform;
            root.SetParent(parent, false);
            var go = Object.Instantiate(prefab, root);
            go.name = "model";
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;

            var rends = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var body = rends.OrderByDescending(r => r.sharedMesh.subMeshCount).ThenByDescending(r => r.sharedMesh.vertexCount).First();
            // the face may be its own renderer; Face00/01 and Eyebrow02 are BA's expression alternates
            var kept = rends.Where(r => r == body || (r.name.Contains("Face_Outline") && !r.name.Contains("Face0"))).ToList();
            foreach (var r in rends) if (!kept.Contains(r)) r.gameObject.SetActive(false);
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true)) mr.gameObject.SetActive(false);   // halos, props

            // 1.2 tall, feet at 0, centred on the hips (as SdRef)
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            body.updateWhenOffscreen = true;
            var wb = body.bounds;
            var lo = root.InverseTransformPoint(wb.min); var hi = root.InverseTransformPoint(wb.max);
            var s = SdRef.Height / Mathf.Max(1e-5f, hi.y - lo.y);
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = new Vector3(-(lo.x + hi.x) * 0.5f * s, -lo.y * s, -(lo.z + hi.z) * 0.5f * s);
            var all = go.GetComponentsInChildren<Transform>(true);
            Transform Find(string n) => all.FirstOrDefault(t => t.name == n);
            var pelvis = Find("Bip001 Pelvis");
            if (pelvis != null) { var pl = root.InverseTransformPoint(pelvis.position); go.transform.localPosition -= new Vector3(pl.x, 0f, pl.z); }

            var k = SdLook.For(heroId);
            var tex = SdSampleTex.For(key, k, Sheet(key, "body"), Sheet(key, "hair"), Sheet(key, "eyemouth"));
            var face = Sheet(key, "face");
            foreach (var r in kept)
            {
                var names = r.sharedMaterials.Select(m => m ? m.name : "").ToArray();
                var em = tex.EyeMouthSrc;
                System.Func<Vector2, float> luma = em == null || !em.isReadable ? null : uv => { var c = em.GetPixelBilinear(uv.x, uv.y); return c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; };
                var (mesh, parts, subNames) = SdFace.Split(r.sharedMesh, names, luma);
                r.sharedMesh = mesh;
                var mats = new Material[mesh.subMeshCount];
                for (var i = 0; i < mats.Length; i++)
                {
                    var n = subNames[i];
                    var eye = parts[i] is SdFace.Part.White or SdFace.Part.Iris or SdFace.Part.Highlight or SdFace.Part.Line or SdFace.Part.Mouth or SdFace.Part.Brow;
                    var sheet = n.Contains("eyemouth") ? tex.EyeMouth : n.Contains("hair") ? tex.Hair : n.Contains("face") || n.Contains("eyebrow") ? face : n.Contains("alpha") ? tex.Body : tex.Body;
                    var m = MeshKit.NewToon(eye ? 0f : 0.005f, sheet);
                    m.SetFloat("_Cutoff", 0f);
                    if (eye) { m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_ShadeStrength", 0.02f); m.SetFloat("_Rim", 0f); }
                    else
                    {
                        m.SetFloat("_ShadeStrength", n.Contains("face") ? 0.06f : 0.24f);
                        m.SetColor("_ShadeTint", n.Contains("hair") ? SdRefLook.ShadeOf(k.Hair) : SdRefLook.WarmShade);
                        m.SetFloat("_Rim", 0.1f);
                    }
                    SdFace.Configure(m, parts[i], SdRef.Height);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            var rig = new ChibiRig { Root = root, Height = SdRef.Height, Model3D = true, RefModel = true, Model = go.transform, ModelScale = go.transform.localScale, ModelPos = go.transform.localPosition };
            rig.Body = pelvis ?? root; rig.Head = Find("Bip001 Head") ?? rig.Body;
            rig.ArmL = Find("Bip001 L UpperArm"); rig.ArmR = Find("Bip001 R UpperArm");
            rig.LegL = Find("Bip001 L Thigh"); rig.LegR = Find("Bip001 R Thigh");
            rig.Spine = Find("Bip001 Spine1") ?? Find("Bip001 Spine");
            rig.ForearmL = Find("Bip001 L Forearm"); rig.ForearmR = Find("Bip001 R Forearm");
            rig.CalfL = Find("Bip001 L Calf"); rig.CalfR = Find("Bip001 R Calf");
            rig.HandL = Find("Bip001 L Hand"); rig.HandR = Find("Bip001 R Hand");
            rig.FootL = Find("Bip001 L Foot"); rig.FootR = Find("Bip001 R Foot");
            rig.Pelvis = pelvis;
            rig.ClavL = Find("Bip001 L Clavicle"); rig.ClavR = Find("Bip001 R Clavicle"); rig.Neck = Find("Bip001 Neck");
            rig.FingersL = new[] { "Bip001 L Finger0", "Bip001 L Finger01", "Bip001 L Finger1", "Bip001 L Finger11", "Bip001 L Finger2", "Bip001 L Finger21" }.Select(Find).ToArray();
            rig.FingersR = new[] { "Bip001 R Finger0", "Bip001 R Finger01", "Bip001 R Finger1", "Bip001 R Finger11", "Bip001 R Finger2", "Bip001 R Finger21" }.Select(Find).ToArray();
            if (rig.Pelvis != null) rig.PelvisRest = rig.Pelvis.localPosition;
            foreach (var r in kept) rig.Renderers.Add(r);
            rig.FaceRenderer = body;
            rig.EyeSub = -1;   // the layered eye is the sample's own: no sheet swaps for expressions (yet)

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.26f, 0f, 0f), new Vector3(0f, 0f, 0.18f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);
            if (k.Glasses || k.Sunglasses) SdRefProps.Glasses(rig, body, root, k.Sunglasses, k.GlassesStyle, k.GlassesColor, layer);
            SdRefProps.HandProp(rig, root, SdRef.RoleOf(heroId), k, layer);

            SdPose.Apply(rig, Pose.Rest);
            if (rig.FootL != null && rig.FootR != null) rig.RestFootY = Mathf.Min(root.InverseTransformPoint(rig.FootL.position).y, root.InverseTransformPoint(rig.FootR.position).y);
            root.gameObject.AddComponent<SdSecondary>().Init(go.transform, "sample:" + key);
            return rig;
        }
    }

    /// <summary>
    /// The sample's sheets in the hero's colours, keeping every stroke of their shading: the hair
    /// by a luminance gradient map onto the hero's hair colour; the iris square (the right three
    /// quarters of the eye sheet) by hue onto the eye colour; the body by colour clusters — each
    /// saturated or dark cluster of the outfit onto one of the hero's outfit colours, skin kept.
    /// </summary>
    public static class SdSampleTex
    {
        public class Set { public Texture2D Body, Hair, EyeMouth, EyeMouthSrc; }
        static readonly Dictionary<string, Set> Cache = new();

        public static Set For(string key, SdLook k, Texture2D body, Texture2D hair, Texture2D eyemouth)
        {
            var id = key + ":" + k.Id;
            if (Cache.TryGetValue(id, out var set)) return set;
            set = new Set { EyeMouthSrc = eyemouth };
            set.Hair = hair != null && hair.isReadable ? GradientMap(hair, k.Hair) : hair;
            set.EyeMouth = eyemouth != null && eyemouth.isReadable ? IrisHue(eyemouth, k.Eye) : eyemouth;
            set.Body = body != null && body.isReadable ? Outfit(body, k) : body;
            return Cache[id] = set;
        }

        static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        static Texture2D Copy(Texture2D src, Color[] px, string name)
        {
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels(px); t.Apply(true, true);
            return t;
        }

        /// <summary>Luminance → the hero's colour: the sheet's darkest to a deep tone, its lightest to a near-white tint.</summary>
        static Texture2D GradientMap(Texture2D src, Color target)
        {
            var px = src.GetPixels();
            float lo = 1f, hi = 0f;
            foreach (var c in px) { var l = Luma(c); lo = Mathf.Min(lo, l); hi = Mathf.Max(hi, l); }
            Color.RGBToHSV(target, out var h, out var sat, out var v);
            // the hero's colour is the sheet's MID-TONE (most of a hair sheet sits there): shadows a
            // deeper tone of it, highlights a lighter one — never lifted to near-white, which turned
            // navy and black hair grey
            var dark = Color.HSVToRGB(h, Mathf.Clamp01(sat * 1.1f + 0.08f), Mathf.Clamp01(v * 0.5f));
            var light = Color.HSVToRGB(h, Mathf.Clamp01(sat * 0.7f), Mathf.Clamp01(v + (1f - v) * 0.45f));
            // where the sheet's mid-tone sits (its median luminance), so that maps to the colour
            var ls = px.Select(Luma).OrderBy(x => x).ToArray(); var mid = Mathf.InverseLerp(lo, hi, ls[ls.Length / 2]);
            for (var i = 0; i < px.Length; i++)
            {
                var t = Mathf.InverseLerp(lo, hi, Luma(px[i]));
                var c = t < mid ? Color.Lerp(dark, target, t / Mathf.Max(0.05f, mid)) : Color.Lerp(target, light, (t - mid) / Mathf.Max(0.05f, 1f - mid));
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+hair");
        }

        /// <summary>The iris square's coloured pixels onto the eye colour's hue; the white and line blocks untouched.</summary>
        static Texture2D IrisHue(Texture2D src, Color eye)
        {
            var px = src.GetPixels(); var w = src.width;
            Color.RGBToHSV(eye, out var eh, out var es, out _);
            for (var i = 0; i < px.Length; i++)
            {
                if ((i % w) < w * 0.22f) continue;
                Color.RGBToHSV(px[i], out _, out var s, out var v);
                if (s < 0.08f) continue;
                var c = Color.HSVToRGB(eh, Mathf.Clamp01(Mathf.Lerp(s, es, 0.6f) + 0.05f), v);
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+iris");
        }

        /// <summary>
        /// The outfit's colours onto the hero's: pixels bucketed by hue (12 × saturated, plus dark
        /// and pale neutrals); skin-like pixels are left; the biggest coloured bucket takes the top
        /// colour, the next the bottom, the next the accent; darks follow the darkest of the hero's
        /// colours and pales stay pale. Shading is kept by carrying each pixel's value ratio.
        /// </summary>
        static Texture2D Outfit(Texture2D src, SdLook k)
        {
            var px = src.GetPixels();
            var bucket = new int[px.Length];
            var count = new int[14];
            for (var i = 0; i < px.Length; i++)
            {
                Color.RGBToHSV(px[i], out var h, out var s, out var v);
                var skin = h > 0.0f && h < 0.11f && s > 0.08f && s < 0.45f && v > 0.62f;
                var b = skin ? -1 : s > 0.22f && v > 0.18f ? Mathf.Min(11, (int)(h * 12f)) : v < 0.35f ? 12 : 13;
                bucket[i] = b; if (b >= 0) count[b]++;
            }
            var order = Enumerable.Range(0, 12).Where(b => count[b] > px.Length / 200).OrderByDescending(b => count[b]).ToList();
            var targets = new[] { k.Top, k.Bottom, k.Accent, k.Shirt };
            var map = new Dictionary<int, Color>();
            for (var j = 0; j < order.Count; j++) map[order[j]] = targets[Mathf.Min(j, targets.Length - 1)];
            // mean value per bucket, so the new colour sits at the same place in the shading
            var mean = new float[14]; var n = new int[14];
            for (var i = 0; i < px.Length; i++) { if (bucket[i] < 0) continue; Color.RGBToHSV(px[i], out _, out _, out var v); mean[bucket[i]] += v; n[bucket[i]]++; }
            for (var b = 0; b < 14; b++) mean[b] = n[b] > 0 ? mean[b] / n[b] : 0.5f;
            var darkest = targets.OrderBy(Luma).First();
            for (var i = 0; i < px.Length; i++)
            {
                var b = bucket[i];
                if (b < 0 || b == 13) continue;
                Color target;
                if (b == 12) target = Luma(darkest) < 0.3f ? darkest : Color.Lerp(darkest, Color.black, 0.5f);
                else if (!map.TryGetValue(b, out target)) continue;
                Color.RGBToHSV(px[i], out _, out _, out var v);
                Color.RGBToHSV(target, out var th, out var ts, out var tv);
                var ratio = v / Mathf.Max(0.05f, mean[b]);
                var c = Color.HSVToRGB(th, ts, Mathf.Clamp01(tv * ratio));
                c.a = px[i].a; px[i] = c;
            }
            return Copy(src, px, src.name + "+outfit");
        }
    }
}
