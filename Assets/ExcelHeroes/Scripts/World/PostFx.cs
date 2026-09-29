using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The 3D views' grade (the Blue Archive cross-check, 2026-09-30: "flat, no lighting depth"): a soft
    /// bloom on the highlights and the effects, a neutral tone curve, a touch more saturation and
    /// contrast, and a light vignette — the look of the reference's bright, glowing streets. One global
    /// volume per layer, built in code (no asset to lose); the camera opts in with <see cref="Use"/>.
    /// </summary>
    public static class PostFx
    {
        static readonly System.Collections.Generic.Dictionary<int, Volume> Volumes = new();

        public static void Use(Camera cam, int layer, float bloom = 0.55f)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask = 1 << layer;
            if (Volumes.TryGetValue(layer, out var v) && v != null) return;
            var go = new GameObject("postfx") { layer = layer };
            Object.DontDestroyOnLoad(go);
            v = go.AddComponent<Volume>();
            v.isGlobal = true; v.priority = 10f;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var b = p.Add<Bloom>(true);
            b.threshold.Override(0.92f); b.intensity.Override(bloom); b.scatter.Override(0.62f); b.tint.Override(new Color(1f, 0.97f, 0.94f));
            var t = p.Add<Tonemapping>(true); t.mode.Override(TonemappingMode.Neutral);
            var c = p.Add<ColorAdjustments>(true);
            c.postExposure.Override(0.08f); c.contrast.Override(10f); c.saturation.Override(14f);
            var vg = p.Add<Vignette>(true); vg.intensity.Override(0.16f); vg.smoothness.Override(0.45f); vg.color.Override(new Color(0.08f, 0.12f, 0.24f));
            v.sharedProfile = p;
            Volumes[layer] = v;
        }
    }
}
