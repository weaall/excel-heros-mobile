using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Builds a playable Windows executable so the game can be run without opening Unity.
    ///
    /// The window is forced to a phone shape: the whole UI is authored for 1080x2400 portrait, and
    /// a desktop build that opens 16:9 landscape shows a correct layout squeezed into the wrong
    /// frame, which looks broken rather than untested.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod ExcelHeroes.EditorTools.BuildGame.Windows
    /// </summary>
    public static class BuildGame
    {
        const string OutDir = "Build/Windows";
        const string Exe = "ExcelHeroes.exe";

        /// <summary>
        /// The actual target. The Windows player is the iteration loop — it builds in fifteen
        /// seconds and the screenshot pass drives it — but an APK is what this game ships as, and a
        /// thing that has never been built for its own platform is not finished.
        ///
        ///   Unity.exe -batchmode -quit -projectPath . -executeMethod ExcelHeroes.EditorTools.BuildGame.Android
        /// </summary>
        [MenuItem("Excel Heroes/Build Android APK")]
        public static void Android()
        {
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "kr.qugo.excelheroes");
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            // Portrait only: the whole layout is a phone held upright, and a landscape frame would
            // put a 2:1 battlefield beside a sheet with no room for either.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory("Build/Android");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = "Build/Android/ExcelHeroes.apk",
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            var s = report.summary;
            Debug.Log($"[BuildGame] Android {s.result} — {s.totalSize / 1024f / 1024f:N1} MB, {s.totalTime.TotalSeconds:N0}s");
            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        [MenuItem("Excel Heroes/Build Windows Player")]
        public static void Windows()
        {
            // A desktop player has no device orientation, so the portrait shape has to come from the
            // default window size instead. Half of 1080x1920 fits on any laptop screen.
            // 20:9, the shape of a current phone — 1080x2400 halved twice. The window used to be
            // 16:9, which is a 2016 device, so everything was being checked against the wrong frame.
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 1200;
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
