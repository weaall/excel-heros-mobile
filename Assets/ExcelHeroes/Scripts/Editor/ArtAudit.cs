using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// What the art actually costs at runtime, as opposed to what it weighs on disk.
    ///
    /// PNG size on disk says almost nothing about a build: Unity re-encodes every texture to a GPU
    /// format at import, so a 500KB PNG can become 4MB in memory (uncompressed RGBA) or 170KB
    /// (compressed). Everything under Resources/ ships whole and is indexed at startup, so this is
    /// the number that decides whether the app is a 60MB download or a 250MB one.
    ///
    /// Run it before deciding to move anything to Addressables — the cheap fix is usually an import
    /// setting, not an architecture change.
    /// </summary>
    public static class ArtAudit
    {
        [MenuItem("Excel Heroes/Audit Art Memory")]
        public static void Run()
        {
            var report = new StringBuilder();
            report.AppendLine("=== art memory (what actually ships) ===");
            report.AppendLine("folder                     count      runtime      on disk  format");

            long grandRuntime = 0, grandDisk = 0;

            // Every folder under Art, not three of six. Monsters, Sheets and Story were missing,
            // so the line this printed as TOTAL was not one — and Monsters is exactly where a
            // compression mistake hides, because a block-compressed pixel strip still loads fine
            // and just looks worse.
            foreach (var folder in new[] { "Art/Cards", "Art/Sprites", "Art/Monsters",
                                           "Art/Sheets", "Art/Story", "Art/Icons", "Art/UI" })
            {
                var sprites = Resources.LoadAll<Sprite>(folder);
                long runtime = 0, disk = 0;
                var formats = new System.Collections.Generic.Dictionary<TextureFormat, int>();

                foreach (var s in sprites)
                {
                    if (s?.texture == null) continue;
                    runtime += Profiler.GetRuntimeMemorySizeLong(s.texture);
                    formats.TryGetValue(s.texture.format, out var n);
                    formats[s.texture.format] = n + 1;

                    var path = AssetDatabase.GetAssetPath(s.texture);
                    if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        disk += new System.IO.FileInfo(path).Length;
                }

                var fmt = string.Join(" ", formats.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}x{kv.Value}"));
                report.AppendLine($"{folder,-24} {sprites.Length,6}  {Mb(runtime),10}  {Mb(disk),10}  {fmt}");

                grandRuntime += runtime;
                grandDisk += disk;
                Resources.UnloadUnusedAssets();
            }

            report.AppendLine(new string('-', 74));
            report.AppendLine($"{"TOTAL",-24} {"",6}  {Mb(grandRuntime),10}  {Mb(grandDisk),10}");
            report.AppendLine();
            report.AppendLine("Everything above is under Resources/, so all of it is in the build and");
            report.AppendLine("loaded into the startup index whether or not the player ever sees it.");

            Debug.Log(report.ToString());
        }

        static string Mb(long bytes) => $"{bytes / 1024f / 1024f:N1} MB";
    }
}
