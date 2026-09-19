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
        // BUILD_OUT lets a verification build go somewhere else, so the screenshot pass can check
        // new work while a copy the user is playing still holds the usual exe open.
        static string OutDir => System.Environment.GetEnvironmentVariable("BUILD_OUT") ?? "Build/Windows";
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
            // Landscape. The fight is a 2:1 side view of a street and the web build it comes from
            // is a desktop window — held upright, the field had to be squeezed into a band with a
            // sky three times too tall above it, and the sheet had a third of its height spare.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;

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
            // A desktop player has no device orientation, so the shape comes from the default
            // window size: 20:9 on its side, which is a current phone held the way this is played.
            PlayerSettings.defaultScreenWidth = 1200;
            PlayerSettings.defaultScreenHeight = 540;
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
