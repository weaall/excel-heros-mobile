using System.IO;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Renders each 3D SD (Resources/Art/SD3D) from four sides with the game's own toon material,
    /// next to its 2D SD, into SD_PREVIEW_OUT (default tools/out/sd3d). Batch mode WITHOUT
    /// -nographics:  Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdPreview.Run
    /// </summary>
    public static class SdPreview
    {
        public static void Run()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SD_PREVIEW_OUT") ?? Path.Combine(Application.dataPath, "..", "tools", "out", "sd3d");
            Directory.CreateDirectory(outDir);
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            foreach (var asset in Resources.LoadAll<TextAsset>("Art/SD3D"))
            {
                var id = asset.name;
                var holder = new GameObject("preview").transform;
                var rig = SdModel.Build(id, holder, 0);
                if (rig == null) { Object.DestroyImmediate(holder.gameObject); continue; }
                const int W = 320, H = 400;
                var sheet = new Texture2D(W * 5, H, TextureFormat.RGB24, false);
                var sprite = Resources.Load<Texture2D>("Art/SD/" + id);
                var yaws = new[] { 180f, 145f, 90f, 0f };
                for (var i = 0; i < yaws.Length; i++)
                {
                    rig.Root.rotation = Quaternion.Euler(0f, yaws[i], 0f);
                    var img = Shoot(rig.Height, W, H);
                    sheet.SetPixels(W * (i + 1), 0, W, H, img.GetPixels());
                    Object.DestroyImmediate(img);
                }
                if (sprite != null)
                {
                    var rt = RenderTexture.GetTemporary(W, H);
                    Graphics.Blit(sprite, rt);
                    RenderTexture.active = rt;
                    var t = new Texture2D(W, H, TextureFormat.RGB24, false);
                    t.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                    sheet.SetPixels(0, 0, W, H, t.GetPixels());
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, id + "_unity.png"), sheet.EncodeToPNG());
                Object.DestroyImmediate(holder.gameObject);
                Debug.Log("[SdPreview] " + id);
            }
        }

        static Texture2D Shoot(float h, int w, int hh)
        {
            var go = new GameObject("cam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = h * 0.56f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.transform.position = new Vector3(0f, h * 0.5f, -5f);
            cam.transform.rotation = Quaternion.identity;
            var rt = new RenderTexture(w, hh, 24);
            cam.targetTexture = rt; cam.aspect = w / (float)hh;
            cam.Render();
            RenderTexture.active = rt;
            var t = new Texture2D(w, hh, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, w, hh), 0, 0); t.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return t;
        }
    }
}
