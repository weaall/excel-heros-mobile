using System.IO;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// The plate camera's view of the empty street set (StreetSet) for each mood, the input the
    /// painter works over (tools/street_plate_gemini.py), and the camera's pose, which
    /// BattleWorld uses to project the painting back onto the set (Plate.shader). The pose is the
    /// battle camera's resting one (BattleWorld.PlaceCamera) with a wider lens, so the painting
    /// covers the camera's travel as it follows the fight.
    ///
    ///   bash tools/unity.sh ExcelHeroes.EditorTools.StreetPlate.Render plate
    ///     → tools/out/plates/street_&lt;mood&gt;.png + plate.json
    /// </summary>
    public static class StreetPlate
    {
        const float Fov = StreetSet.PlateFov, Aspect = StreetSet.PlateAspect;
        static (Vector3 pos, Quaternion rot) Pose() => StreetSet.PlatePose();

        public static void Render()
        {
            var outDir = Path.Combine(Application.dataPath, "..", "tools", "out", "plates");
            Directory.CreateDirectory(outDir);
            Shader.SetGlobalVector("_EhLightDir", new Vector4(-0.45f, 0.85f, -0.5f, 0f));
            var (pos, rot) = Pose();
            for (var mood = 0; mood < 3; mood++)
            {
                var holder = new GameObject("plate").transform;
                var street = StreetSet.Build(holder, BattleWorld.Layer, mood);
                var go = new GameObject("platecam");
                var cam = go.AddComponent<Camera>();
                cam.cullingMask = 1 << BattleWorld.Layer;
                cam.fieldOfView = Fov; cam.aspect = Aspect; cam.nearClipPlane = 0.3f; cam.farClipPlane = 80f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = mood == 2 ? new Color(0.16f, 0.18f, 0.32f) : new Color(0.72f, 0.84f, 0.96f);
                go.transform.SetPositionAndRotation(pos, rot);
                const int W = 2048, H = 1152;
                var rt = new RenderTexture(W, H, 24) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var t = new Texture2D(W, H, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(outDir, $"street_{mood}.png"), t.EncodeToPNG());
                Object.DestroyImmediate(t); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); Object.DestroyImmediate(holder.gameObject);
            }
            File.WriteAllText(Path.Combine(outDir, "plate.json"), JsonUtility.ToJson(new PlatePose { pos = pos, rot = rot, fov = Fov, aspect = Aspect }, true));
            Debug.Log("[StreetPlate] rendered 3 moods");
        }

        [System.Serializable] public class PlatePose { public Vector3 pos; public Quaternion rot; public float fov, aspect; }
    }
}
