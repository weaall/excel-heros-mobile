using System.Linq;
using ExcelHeroes.World;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Can the SD rig be a Unity Humanoid? Builds cfo's figure, builds the avatar, reports
    /// isValid / isHuman and any Biped bone the map could not find.
    ///   Unity -batchmode -quit -executeMethod ExcelHeroes.EditorTools.SdHumanoidTest.Run
    /// </summary>
    public static class SdHumanoidTest
    {
        public static void Run()
        {
            if (!ExcelHeroes.Data.GameData.Loaded) ExcelHeroes.Data.GameData.Load();
            foreach (var id in (System.Environment.GetEnvironmentVariable("SD_IDS") ?? "cfo,intern").Split(','))
            {
                var holder = new GameObject("ht").transform;
                var rig = SdRef.Build(id, holder, 0);
                if (rig == null || rig.Model == null) { Debug.Log($"[Humanoid] {id}: no rig/model"); continue; }
                var missing = SdHumanoid.MissingBones(rig.Model).ToList();
                var av = SdHumanoid.Build(rig.Model, id);
                Debug.Log($"[Humanoid] {id}: valid {av.isValid} human {av.isHuman} missing [{string.Join(", ", missing)}] model '{rig.Model.name}'");
                Object.DestroyImmediate(holder.gameObject);
            }
        }
    }
}
