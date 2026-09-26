using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace ExcelHeroes.World
{
    /// <summary>
    /// Renders a 3D figure into a small transparent texture, for places that want the model as a
    /// picture — the error codex tiles. The texture is handed back at once (empty) and filled a
    /// couple of frames later: a queue sets one figure up, lets the camera draw it with the normal
    /// frame (the path the 2D Renderer is known to take — a one-off render request came back
    /// blank), reads it back and moves on. Cached for the session.
    /// </summary>
    public class Snapshot3D : MonoBehaviour
    {
        const int Layer = 28;
        const int Size = 256;
        static Snapshot3D _instance;
        static readonly Dictionary<string, Texture2D> Cache = new();

        Camera _cam;
        RenderTexture _rt;
        readonly Queue<(Func<(Transform, float)> make, Texture2D into, Action done)> _jobs = new();
        (Transform fig, Texture2D into, Action done)? _current;
        int _wait;

        static Snapshot3D Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Snapshot3D");
                DontDestroyOnLoad(go);
                go.transform.position = new Vector3(0f, -900f, 0f);
                _instance = go.AddComponent<Snapshot3D>();
                _instance.Init();
                return _instance;
            }
        }

        void Init()
        {
            var camGo = new GameObject("SnapshotCamera") { layer = Layer };
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 20f;
            _cam.depth = -12;
            _cam.enabled = false;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "SnapshotRT" };
            _rt.Create();
            _cam.targetTexture = _rt;
        }

        /// <summary>A monster's portrait; `done` runs once the pixels are in.</summary>
        public static Texture2D Monster(string typeId, string shape, bool boss, Action done = null)
        {
            var key = $"m:{typeId}:{shape}:{boss}";
            if (Cache.TryGetValue(key, out var t) && t != null) { done?.Invoke(); return t; }
            t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "snap:" + key, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(new Color32[Size * Size]);
            t.Apply();
            Cache[key] = t;
            var self = Instance;
            self._jobs.Enqueue((() =>
            {
                var fig = MonsterBuilder.Build(typeId ?? "x", shape ?? "blob", boss, self.transform, Layer, out var h);
                return (fig, h);
            }, t, done));
            return t;
        }

        void Update()
        {
            if (_current.HasValue && --_wait > 0) return;
            if (_current.HasValue)
            {
                // Drawn last frame: read it back.
                var (fig, into, done) = _current.Value;
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                into.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                into.Apply();
                RenderTexture.active = prev;
                if (fig != null) Destroy(fig.gameObject);
                _current = null;
                _cam.enabled = false;
                done?.Invoke();
            }

            if (_jobs.Count == 0) return;
            var job = _jobs.Dequeue();
            var (figure, height) = job.make();
            SetLayer(figure, Layer);
            figure.localPosition = Vector3.zero;
            figure.localRotation = Quaternion.Euler(0f, 142f, 0f);
            _cam.orthographicSize = Mathf.Max(0.3f, height * 0.62f);
            _cam.aspect = 1f;
            // tilted 10° down from 6 m back: lift the camera by the tilt so the figure stays centred
            _cam.transform.localPosition = new Vector3(0f, height * 0.55f + 6f * Mathf.Tan(10f * Mathf.Deg2Rad), -6f);
            _cam.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            _cam.enabled = true;
            _current = (figure, job.into, job.done);
            // A camera switched on draws nothing into its target on its first frame under the
            // 2D Renderer; read on the second.
            _wait = 2;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }
    }
}
