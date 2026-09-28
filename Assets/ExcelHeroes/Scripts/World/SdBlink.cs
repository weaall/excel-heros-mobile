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
        struct Eye { public Transform Plates, Lid; public Vector3 LidRest, Drop; }
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
                b._eyes[i] = new Eye { Plates = bone, Lid = shared ? null : lid, LidRest = lid != null ? lid.localPosition : Vector3.zero, Drop = lid != null && lid.parent != null ? lid.parent.InverseTransformVector(drop) : Vector3.zero };
            }
            if (hold != null && float.TryParse(hold, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h)) { b.Hold = h; b.Set(h); }
            b._next = Random.Range(1f, 4f);
            return b;
        }

        /// <summary>How shut the eyes are for an expression: happy and dizzy as BA's closed ^^ eyes,
        /// hurt a wince, angry a narrowed glare.</summary>
        public void Express(string expr) => Squint = expr switch { "happy" => 1f, "dizzy" => 1f, "hurt" => 0.7f, "angry" => 0.3f, _ => 0f };

        void Set(float t)
        {
            var y = Mathf.Lerp(1f, 0.1f, t);
            foreach (var e in _eyes)
            {
                if (e.Plates != null) e.Plates.localScale = new Vector3(1f, y, 1f);
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
