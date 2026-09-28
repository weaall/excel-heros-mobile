using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Quaternius' Universal Animation Library (CC0, Assets/ExcelHeroes/Anim/UAL): imported as a
    /// Humanoid so its clips retarget onto the SD rig (World/SdHumanoid), every take split into its
    /// own clip. List writes the clip names and lengths to tools/out/ual_clips.txt.
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.UalImport.Setup
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.UalImport.List
    /// </summary>
    public static class UalImport
    {
        public const string Fbx = "Assets/ExcelHeroes/Resources/Anim/UAL1_Standard.fbx";
        public const string Fbx2 = "Assets/ExcelHeroes/Resources/Anim/UAL2_Standard.fbx";
        static readonly string[] All = { Fbx, Fbx2 };

        public static void Setup() { foreach (var f in All) SetupOne(f); }

        static void SetupOne(string Fbx)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            if (imp == null) { Debug.LogError("[UAL] no fbx at " + Fbx); return; }
            imp.importAnimation = true;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importBlendShapes = false; imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            // one clip per take, looping where the take is a cycle
            var takes = imp.importedTakeInfos;
            imp.clipAnimations = takes.Select(t =>
            {
                var n = t.name.ToLowerInvariant();
                var loop = n.Contains("loop") || n.Contains("idle") || n.Contains("walk") || n.Contains("run") || n.Contains("jog") || n.Contains("sprint");
                return new ModelImporterClipAnimation
                {
                    name = t.name, takeName = t.name, firstFrame = t.startTime * t.sampleRate, lastFrame = t.stopTime * t.sampleRate,
                    loopTime = loop, loopPose = loop, lockRootRotation = true, lockRootHeightY = true, lockRootPositionXZ = true,
                    keepOriginalOrientation = true, keepOriginalPositionY = true, keepOriginalPositionXZ = true,
                };
            }).ToArray();
            imp.SaveAndReimport();
            Debug.Log($"[UAL] set up: {takes.Length} takes, avatar human {((Avatar)AssetDatabase.LoadAllAssetsAtPath(Fbx).FirstOrDefault(a => a is Avatar))?.isHuman}");
        }

        public static void List()
        {
            var clips = All.SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f).OfType<AnimationClip>()).Where(c => !c.name.StartsWith("__preview")).ToList();
            var sb = new StringBuilder();
            foreach (var c in clips.OrderBy(c => c.name)) sb.AppendLine($"{c.name}\t{c.length:F2}s\tloop {c.isLooping}\thuman {c.isHumanMotion}");
            File.WriteAllText(Path.Combine(Application.dataPath, "..", "tools", "out", "ual_clips.txt"), sb.ToString());
            Debug.Log($"[UAL] {clips.Count} clips");
        }
    }
}
