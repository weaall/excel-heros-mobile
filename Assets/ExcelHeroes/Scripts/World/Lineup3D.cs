using System.Collections.Generic;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using ExcelHeroes.UI;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace ExcelHeroes.World
{
    /// <summary>
    /// The squad line-up in 3D, for 편성 — the reference's 부대 편성 shows its members as SD models
    /// standing in a row, not as cards or illustrations. The same figures as the battle, rendered
    /// by an orthographic camera into a transparent texture that sits behind the slot plates.
    ///
    /// It animates itself (breathing, the sheet bobbing and turning to the camera) and stops its
    /// camera the moment its host element leaves the panel, so a closed screen costs nothing.
    /// </summary>
    public class Lineup3D : MonoBehaviour
    {
        const int Layer = 29;
        static Lineup3D _instance;

        Camera _cam;
        RenderTexture _rt;
        Transform _cast;
        readonly List<(ChibiRig rig, float phase)> _figs = new();
        readonly Dictionary<string, GameObject> _templates = new();
        Transform _templateRoot;
        VisualElement _host;
        float _t;

        public static Lineup3D Instance
        {
            get
            {
                if (_instance != null) return _instance;
                var go = new GameObject("Lineup3D");
                DontDestroyOnLoad(go);
                go.transform.position = new Vector3(0f, -600f, 0f);
                _instance = go.AddComponent<Lineup3D>();
                _instance.Init();
                return _instance;
            }
        }

        void Init()
        {
            _templateRoot = new GameObject("templates").transform;
            _templateRoot.SetParent(transform, false);
            _templateRoot.gameObject.SetActive(false);
            _cast = new GameObject("cast").transform;
            _cast.SetParent(transform, false);

            var camGo = new GameObject("LineupCamera") { layer = Layer };
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.cullingMask = 1 << Layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 40f;
            _cam.depth = -11;
            _cam.enabled = false;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        /// <summary>
        /// Stands `ids` in a row, one per slot, and returns the texture to put behind the slots.
        /// `floor` is where the feet go, as a fraction of the host's height from the top.
        /// </summary>
        public RenderTexture Show(VisualElement host, IList<string> ids, IList<float> centres, float slotW, int pxW, int pxH, float floor)
        {
            _host = host;
            pxW = Mathf.Clamp(pxW, 64, 4096);
            pxH = Mathf.Clamp(pxH, 64, 4096);
            if (_rt == null || _rt.width != pxW || _rt.height != pxH)
            {
                if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(pxW, pxH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "LineupRT" };
                _rt.Create();
                _cam.targetTexture = _rt;
            }

            foreach (var f in _figs) if (f.rig?.Root != null) Destroy(f.rig.Root.gameObject);
            _figs.Clear();

            var aspect = pxW / (float)pxH;
            const float worldH = 2.2f;
            var worldW = worldH * aspect;
            _cam.orthographicSize = worldH * 0.5f;
            _cam.aspect = aspect;
            _cam.transform.localPosition = new Vector3(0f, 0f, -12f);
            _cam.transform.localRotation = Quaternion.identity;

            var feetY = worldH * 0.5f - floor * worldH;
            // As tall as the space above the plate allows, and never wider than a slot.
            var scale = Mathf.Min(floor * worldH * 0.86f / 0.95f, slotW * worldW * 1.9f);

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (string.IsNullOrEmpty(id)) continue;
                // the SD sprite when it exists, the built model otherwise
                var rig = SdRef.Build(id, _cast, Layer) ?? SdBase.Build(id, _cast, Layer) ?? SdSprite.Build(id, _cast, Layer);
                if (rig == null)
                {
                    var go = Instantiate(Template(id), _cast);
                    go.SetActive(true);
                    SetLayer(go.transform, Layer);
                    rig = ChibiRig.Bind(go.transform, 0.95f);
                }
                var def = GameData.Hero(id);
                var owned = Game.Player?.Find(id);
                var spec = BackSheet.For(def, owned);
                if (!rig.Sprite) ChibiBuilder.AddSheet(rig, SheetTexture.For(spec, id), spec.Left ? 1 : -1, Layer);
                SetLayer(rig.Root, Layer);
                rig.Root.localPosition = new Vector3((centres[i] - 0.5f) * worldW, feetY, 0f);
                var s = rig.Sprite || rig.Model3D ? Mathf.Min(floor * worldH * 1.02f / rig.Height, slotW * worldW * 1.6f) : scale;
                rig.Root.localScale = Vector3.one * s;
                rig.Root.localRotation = rig.Sprite ? Quaternion.identity : rig.Model3D ? Quaternion.Euler(0f, 180f + 20f, 0f) : Quaternion.Euler(0f, 68f, 0f);
                _figs.Add((rig, i * 1.3f));
            }
            _cam.enabled = true;
            return _rt;
        }

        GameObject Template(string id)
        {
            if (_templates.TryGetValue(id, out var t) && t != null) return t;
            var doll = DollData.For(id);
            t = ChibiBuilder.Build(doll, _templateRoot, Layer, id == GameData.MainId).gameObject;
            SetLayer(t.transform, Layer);
            _templates[id] = t;
            return t;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        void Update()
        {
            if (_host == null || _host.panel == null)
            {
                if (_cam != null) _cam.enabled = false;
                return;
            }
            _cam.enabled = true;
            _t += Time.deltaTime;
            foreach (var (rig, phase) in _figs)
            {
                if (rig?.Root == null) continue;
                var br = Mathf.Sin(_t * 2.2f + phase);
                if (rig.Model3D)
                {
                    // the 3D SD: breathing on the bones, a slow sway, the sheet behind the back
                    if (rig.RefModel)
                    {
                        rig.Pose(rig.Body, Quaternion.Euler(0f, 0f, -br * 1.5f) * Quaternion.Euler(Mathf.Sin(_t * 0.6f + phase) * 4f, 0f, 0f));
                        rig.Pose(rig.Head, Quaternion.Euler(0f, 0f, -br * 2f) * Quaternion.Euler(Mathf.Sin(_t * 0.7f + phase) * 3f, 0f, 0f));
                        rig.Pose(rig.ArmL, Quaternion.Euler(0f, 0f, br * 3f));
                        rig.Pose(rig.ArmR, Quaternion.Euler(0f, 0f, -br * 3f));
                    }
                    else
                    {
                        rig.Body.localRotation = Quaternion.Euler(br * 1.5f, Mathf.Sin(_t * 0.6f + phase) * 4f, 0f);
                        if (rig.Head != null) rig.Head.localRotation = Quaternion.Euler(br * 2f, 0f, Mathf.Sin(_t * 0.7f + phase) * 3f);
                        if (rig.ArmL != null) rig.ArmL.localRotation = Quaternion.Euler(0f, 0f, 6f + br * 3f);
                        if (rig.ArmR != null) rig.ArmR.localRotation = Quaternion.Euler(0f, 0f, -6f - br * 3f);
                    }
                    if (rig.Sheet != null)
                    {
                        rig.Sheet.localPosition = new Vector3(rig.SheetSide * 0.12f, rig.Height * 0.62f + Mathf.Sin(_t * 1.6f + phase) * 0.015f, -0.14f);
                        rig.Sheet.localRotation = Quaternion.Euler(0f, 180f, rig.SheetSide * 12f);
                    }
                    continue;
                }
                if (rig.Sprite)
                {
                    // breathing, and a small hop now and then
                    var hop = Mathf.Max(0f, Mathf.Sin(_t * 1.1f + phase * 2f) - 0.96f) * 25f;
                    rig.Body.localScale = new Vector3(1f - br * 0.012f, 1f + br * 0.018f, 1f);
                    rig.Body.localPosition = new Vector3(0f, Mathf.Sin(hop * Mathf.PI) * 0.06f, 0f);
                    if (rig.Sheet != null)
                        rig.Sheet.localPosition = ChibiBuilder.SpriteSheetSpot(rig, rig.SheetSide) + new Vector3(0f, Mathf.Sin(_t * 1.6f + phase) * 0.02f, 0f);
                    continue;
                }
                if (rig.Body != null) rig.Body.localPosition = new Vector3(0f, br * 0.004f, 0f);
                if (rig.Head != null) rig.Head.localRotation = Quaternion.Euler(br * 2f, 0f, Mathf.Sin(_t * 0.7f + phase) * 3f);
                if (rig.ArmL != null) rig.ArmL.localRotation = Quaternion.Euler(-8f, 0f, br * 3f);
                if (rig.ArmR != null) rig.ArmR.localRotation = Quaternion.Euler(8f, 0f, -br * 3f);
                if (rig.Sheet != null)
                {
                    rig.Sheet.localPosition = new Vector3(-0.17f, 0.66f + Mathf.Sin(_t * 1.6f + phase) * 0.012f, rig.SheetSide * 0.13f);
                    rig.Sheet.rotation = _cam.transform.rotation * Quaternion.Euler(0f, 0f, rig.SheetSide * 10f);
                }
            }
        }
    }
}
