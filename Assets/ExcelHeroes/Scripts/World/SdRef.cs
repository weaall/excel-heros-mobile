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
            // the face is on +Z of the FBX already (RefAnalyze: EyeMouth at z > 0) — our forward
            rends = new[] { body };

            var look = SdRefLook.For(heroId);
            // the hair submesh, found by the SAMPLE's material names before they are replaced
            var hairSub = -1;
            for (var i = 0; i < body.sharedMaterials.Length; i++)
                if (body.sharedMaterials[i] && body.sharedMaterials[i].name.ToLowerInvariant().Contains("hair")) hairSub = i;
            SdRefHair.Apply(body, look.Style, hairSub);
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
            rig.ArmL = Find("Bip001 L UpperArm"); rig.ArmR = Find("Bip001 R UpperArm");
            rig.LegL = Find("Bip001 L Thigh"); rig.LegR = Find("Bip001 R Thigh");
            rig.Spine = Find("Bip001 Spine1") ?? Find("Bip001 Spine");
            rig.ForearmL = Find("Bip001 L Forearm"); rig.ForearmR = Find("Bip001 R Forearm");
            rig.CalfL = Find("Bip001 L Calf"); rig.CalfR = Find("Bip001 R Calf");
            foreach (var r in rends) if (r.gameObject.activeSelf) rig.Renderers.Add(r);

            var sh = new MeshKit.Builder();
            sh.Quad(new Vector3(0f, 0.004f, 0f), new Vector3(0.26f, 0f, 0f), new Vector3(0f, 0f, 0.18f), new Color(0.1f, 0.14f, 0.25f, 0.4f));
            MeshKit.Part("shadow", root, sh.Bake("shadow"), ChibiBuilder.ShadowMat, layer);
            return rig;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
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

        public static SdRefLook For(string heroId)
        {
            if (Cache.TryGetValue(heroId, out var l)) return l;
            return Cache[heroId] = new SdRefLook(SdLook.For(heroId));
        }

        static Texture2D Tex(string n) => Resources.Load<Texture2D>("Art/SDBase/" + n);

        public Material MaterialFor(string matName)
        {
            var key = matName.Contains("eyemouth") ? "eyemouth" : matName.Contains("eyebrow") ? "eyebrow"
                    : matName.Contains("hair") ? "hair" : matName.Contains("face") ? "face" : "body";
            if (_mats.TryGetValue(key, out var m)) return m;
            switch (key)
            {
                case "hair":
                    // the sample's hair texture is a flat colour + highlight: recolour to ours
                    m = MeshKit.NewToon(0.004f, Tex("base_hair"));
                    m.SetColor("_Color", MeshKit.Lin(_k.Hair));
                    break;
                case "eyemouth":
                case "eyebrow":
                    // the sample's eye quads UV onto iris / white / mouth pieces on a black ground;
                    // our own painting of that layout (SdRefTex), no hull
                    m = MeshKit.NewToon(0f, SdRefTex.EyeMouth(_k));
                    m.SetFloat("_Cutoff", 0.0f);
                    m.SetFloat("_OutlineWidth", 0f);
                    m.SetFloat("_ShadeStrength", 0.05f);
                    m.SetFloat("_Rim", 0f);
                    break;
                case "face":
                    m = MeshKit.NewToon(0.004f, SdRefTex.Face(_k));
                    break;
                default:
                    m = MeshKit.NewToon(0.004f, Tex("base_body"));
                    m.SetColor("_Color", MeshKit.Lin(Color.Lerp(Color.white, _k.Top, 0.6f)));
                    break;
            }
            m.SetFloat("_ShadeStrength", 0.22f);
            m.SetFloat("_Rim", 0.1f);
            return _mats[key] = m;
        }
    }
}
