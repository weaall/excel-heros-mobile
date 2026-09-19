using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Builds a playable Windows executable so the game can be run without opening Unity.
    ///
    /// The window is forced to a phone shape: the whole UI is authored for 1080x1920 portrait, and
    /// a desktop build that opens 16:9 landscape shows a correct layout squeezed into the wrong
    /// frame, which looks broken rather than untested.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod ExcelHeroes.EditorTools.BuildGame.Windows
    /// </summary>
    public static class BuildGame
    {
        const string OutDir = "Build/Windows";
        const string Exe = "ExcelHeroes.exe";

        [MenuItem("Excel Heroes/Build Windows Player")]
        public static void Windows()
        {
            // A desktop player has no device orientation, so the portrait shape has to come from the
            // default window size instead. Half of 1080x1920 fits on any laptop screen.
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[BuildGame] no scenes in Build Settings — run Excel Heroes ▸ Rebuild Project Setup first.");
                EditorApplication.Exit(1);
                return;
            }

            Directory.CreateDirectory(OutDir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutDir, Exe),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var s = report.summary;
            Debug.Log($"[BuildGame] {s.result} — {s.totalSize / 1024f / 1024f:N1} MB, " +
                      $"{s.totalTime.TotalSeconds:N0}s, {scenes.Length} scene(s) -> {Path.GetFullPath(OutDir)}");

            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
