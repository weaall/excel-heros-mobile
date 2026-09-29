using System.Collections.Generic;
using UnityEngine;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The sample face's blink, as BA does it: the upper-lid skin slides down over the eye (the
    /// lid bone found per eye by SdSample.EyeBones — the bone the skin just over it rides), while
    /// the eye's plates, on a bone of their own at the eye's lower edge, squash flat so the lash
    /// line folds onto the lower lid as the closed-eye stroke. Blinks every 2.2–5 s (a double blink
    /// now and then); `Squint` holds the eyes part-shut (the battle's expression calls), `Hold`
    /// fixes them (previews: SD_LID=0..1). After the pose, so the animation never fights it.
    /// </summary>
    public class SdBlink : MonoBehaviour
    {
        struct Eye { public Transform Plates, Lid; public Vector3 LidRest, Drop, PlateScale; }
        Eye[] _eyes;
        float _next, _t0 = -1f;
        public float Squint;
        public float Hold = -1f;

        public static SdBlink Attach(GameObject go, IList<(Transform bone, Transform lid, Vector3 drop)> eyes, string hold)
        {
            var b = go.AddComponent<SdBlink>();
            b._eyes = new Eye[eyes.Count];
            for (var i = 0; i < eyes.Count; i++)
            {
                var (bone, lid, drop) = eyes[i];
                // one lid bone may serve both eyes (a shared brow-lid): move it once
                var shared = false; for (var j = 0; j < i; j++) if (b._eyes[j].Lid == lid) shared = true;
                b._eyes[i] = new Eye { Plates = bone, PlateScale = bone.localScale, Lid = shared ? null : lid, LidRest = lid != null ? lid.localPosition : Vector3.zero, Drop = lid != null && lid.parent != null ? lid.parent.InverseTransformVector(drop) : Vector3.zero };
            }
            if (hold != null && float.TryParse(hold, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)) { b.Hold = h; b.Set(h); }
            b._next = Random.Range(1f, 4f);
            return b;
        }

        /// <summary>How shut the eyes are for an expression: happy and dizzy as BA's closed ^^ eyes,
        /// hurt a wince, angry a narrowed glare.</summary>
        public void Express(string expr)
        {
            Squint = expr switch { "smile" => 0.2f, "happy" => 1f, "dizzy" => 1f, "hurt" => 0.7f, "angry" => 0.3f, _ => 0f };
            Set(Hold >= 0f ? Hold : Squint);
            if (_mouth == null) return;
            var tex = expr switch { "smile" => Mouth(0), "happy" => Mouth(0), "hurt" => Mouth(1), "dizzy" => Mouth(1), "angry" => Mouth(2), _ => null };
            _mouth.enabled = tex != null;
            if (tex != null) _mouth.sharedMaterial.SetTexture("_MainTex", tex);
        }

        MeshRenderer _mouth;

        /// <summary>A small mouth for the expressions, on the face where the sample's mouth plate is:
        /// an open smile (happy), a wavy wince (hurt, dizzy), a small frown (angry); none at rest —
        /// the face sheet's own line is the closed mouth.</summary>
        public void AddMouth(Transform head, Vector3 at, Transform root, int layer)
        {
            if (head == null) return;
            var b = new MeshKit.Builder();
            var h = SdRef.Height;
            b.Quad(Vector3.zero, Vector3.right * h * 0.022f, Vector3.up * h * 0.016f, Color.white);
            var m = MeshKit.NewToon(0f, Mouth(0));
            m.SetFloat("_Cutoff", 0.5f); m.SetFloat("_ShadeStrength", 0f); m.SetFloat("_Rim", 0f);
            m.SetFloat("_DepthPull", 0.03f * h); m.renderQueue = 2005;
            var go = MeshKit.Part("mouth", head, b.Bake("mouth"), m, layer);
            var ls = head.lossyScale; go.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
            go.transform.position = at + root.forward * h * 0.004f - root.up * h * 0.004f;
            go.transform.rotation = root.rotation;
            _mouth = go.GetComponent<MeshRenderer>(); _mouth.enabled = false;
        }

        static readonly Texture2D[] Mouths = new Texture2D[3];

        static Texture2D Mouth(int kind)
        {
            if (Mouths[kind] != null) return Mouths[kind];
            const int W = 64, H = 48;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "mouth" + kind, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            var line = new Color(0.36f, 0.14f, 0.16f); var inside = new Color(0.62f, 0.2f, 0.24f); var tongue = new Color(0.95f, 0.55f, 0.58f);
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;   // -1..1, v up
                    var c = new Color(0, 0, 0, 0);
                    if (kind == 0)
                    {
                        // an open smile: a D on its back — flat top at v 0.35, round bottom
                        var r = u * u / 0.55f + (v - 0.35f) * (v - 0.35f) / 0.9f;
                        if (v < 0.4f && r < 1f) c = r > 0.72f || v > 0.3f ? line : v < -0.35f && Mathf.Abs(u) < 0.45f ? tongue : inside;
                    }
                    else if (kind == 1)
                    {
                        // a wavy wince
                        var wv = Mathf.Sin(u * Mathf.PI * 2.2f) * 0.22f;
                        if (Mathf.Abs(u) < 0.75f && Mathf.Abs(v - wv) < 0.13f) c = line;
                    }
                    else
                    {
                        // a small frown: an arch
                        var av = 0.25f - u * u * 0.7f;
                        if (Mathf.Abs(u) < 0.55f && Mathf.Abs(v - av) < 0.12f) c = line;
                    }
                    px[y * W + x] = c;
                }
            t.SetPixels(px); t.Apply(false, true);
            return Mouths[kind] = t;
        }

        void Set(float t)
        {
            var y = Mathf.Lerp(1f, 0.1f, t);
            foreach (var e in _eyes)
            {
                // the bone's rest scale cancels the head's import scale (×124): squash relative to it
                if (e.Plates != null) e.Plates.localScale = new Vector3(e.PlateScale.x, e.PlateScale.y * y, e.PlateScale.z);
                if (e.Lid != null) e.Lid.localPosition = e.LidRest + e.Drop * t;
            }
        }

        void LateUpdate()
        {
            if (Hold >= 0f) { Set(Hold); return; }
            var now = Time.time;
            var blink = 0f;
            if (_t0 < 0f && now >= _next) _t0 = now;
            if (_t0 >= 0f)
            {
                var e = (now - _t0) / 0.14f;
                if (e >= 1f) { _t0 = -1f; _next = now + (Random.value < 0.15f ? 0.18f : Random.Range(2.2f, 5f)); }
                else blink = e < 0.4f ? e / 0.4f : 1f - (e - 0.4f) / 0.6f;
            }
            Set(Mathf.Max(Squint, blink));
        }
    }
}
