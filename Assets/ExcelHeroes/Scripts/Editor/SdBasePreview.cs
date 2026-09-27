using System.IO;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Line-up of characters on the common SD base (World/SdBase), front / three-quarter / side,
    /// one row per angle, into SD_PREVIEW_OUT/sdbase_lineup.png. Batch mode without -nographics:
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdBasePreview.Run
    /// SD_IDS=a,b,c picks the cast (default: a spread of hair styles and outfits).
    /// </summary>
    public static class SdBasePreview
    {
        public static void Run()
        {
            var outDir = System.Environment.GetEnvironmentVariable("SD_PREVIEW_OUT") ?? Path.Combine(Application.dataPath, "..", "tools", "out", "sd3d");
            Directory.CreateDirectory(outDir);
            var ids = (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "intern,cfo,ceo,vlookup,hr_jung,guard,barista,cto,macro,welfare").Split(',');
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            const int W = 220, H = 300;
            float[] yaws = { 180f, 145f, 90f };
            var sheet = new Texture2D(W * ids.Length, H * yaws.Length, TextureFormat.RGB24, false);
            for (var i = 0; i < ids.Length; i++)
            {
                var holder = new GameObject("preview").transform;
                var rig = SdRef.Build(ids[i], holder, 0) ?? SdBase.Build(ids[i], holder, 0);
                for (var a = 0; a < yaws.Length; a++)
                {
                    rig.Root.rotation = Quaternion.Euler(0f, yaws[a], 0f);
                    var img = Shoot(SdBase.Height, W, H);
                    sheet.SetPixels(W * i, H * (yaws.Length - 1 - a), W, H, img.GetPixels());
                    Object.DestroyImmediate(img);
                }
                Object.DestroyImmediate(holder.gameObject);
            }
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "sdbase_lineup.png"), sheet.EncodeToPNG());
            Debug.Log("[SdBasePreview] done");
        }

        static Texture2D Shoot(float h, int w, int hh)
        {
            var go = new GameObject("cam");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = h * 0.55f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.transform.position = new Vector3(0f, h * 0.5f, -5f);
            var rt = new RenderTexture(w, hh, 24) { antiAliasing = 4 };
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
