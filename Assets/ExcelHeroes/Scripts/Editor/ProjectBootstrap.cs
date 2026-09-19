using System.IO;
using System.Linq;
using ExcelHeroes.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// One-time project wiring so the repo can hold only text files and still open as a running game:
    /// creates the PanelSettings asset, builds the Main scene with a UIDocument + AppRoot, registers
    /// it in Build Settings and locks the player to portrait.
    ///
    /// It runs once on first load and then never touches anything again — every step is guarded by
    /// "does this already exist". Re-run it by hand from Excel Heroes ▸ Rebuild Project Setup.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectBootstrap
    {
        const string ScenePath = "Assets/ExcelHeroes/Scenes/Main.unity";
        const string PanelPath = "Assets/ExcelHeroes/UI/PanelSettings.asset";
        const string ThemePath = "Assets/ExcelHeroes/UI/ExcelHeroesTheme.tss";
        const string UxmlPath = "Assets/ExcelHeroes/UI/AppShell.uxml";
        const string DonePrefKey = "ExcelHeroes.BootstrapDone.v1";

        static ProjectBootstrap()
        {
            if (SessionState.GetBool(DonePrefKey, false)) return;
            SessionState.SetBool(DonePrefKey, true);
            EditorApplication.delayCall += () => Run(interactive: false);
        }

        [MenuItem("Excel Heroes/Rebuild Project Setup")]
        public static void RunFromMenu() => Run(interactive: true);

        /// <summary>
        /// Re-attaches PanelSettings to the scene's UIDocument and saves.
        ///
        /// This exists because the built game came up running, logging and throwing nothing — and
        /// completely blank. UIDocument had its UXML but its panelSettings was null, and a UIDocument
        /// without PanelSettings renders nothing at all. Nothing in the project reports that: it is
        /// not an error, just an empty screen.
        ///
        /// The original assignment did not survive scene serialisation, so this sets it, marks both
        /// the component and the scene dirty, and verifies the reference is actually on disk
        /// afterwards rather than assuming the save worked.
        /// </summary>
        [MenuItem("Excel Heroes/Repair Scene Wiring")]
        public static void RepairScene()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (panel == null || uxml == null)
            {
                Debug.LogError($"[ExcelHeroes] missing asset — panel={panel != null} uxml={uxml != null}");
                EditorApplication.Exit(1);
                return;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var doc = Object.FindFirstObjectByType<UIDocument>();
            if (doc == null)
            {
                var go = new GameObject("App");
                doc = go.AddComponent<UIDocument>();
                go.AddComponent<AppRoot>();
            }
            if (doc.GetComponent<AppRoot>() == null) doc.gameObject.AddComponent<AppRoot>();

            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;

            EditorUtility.SetDirty(doc);
            EditorUtility.SetDirty(doc.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // Verify against the file rather than trusting the setter — that is what went wrong.
            var panelGuid = AssetDatabase.AssetPathToGUID(PanelPath);
            var text = System.IO.File.ReadAllText(ScenePath);
            var wired = text.Contains(panelGuid);
            Debug.Log($"[ExcelHeroes] scene repaired — panelSettings on disk: {wired} (guid {panelGuid})");
            if (!wired) EditorApplication.Exit(1);
        }

        static void Run(bool interactive)
        {
            var panel = EnsurePanelSettings();
            if (panel == null) return;

            var created = EnsureScene(panel);
            EnsureBuildSettings();
            EnsurePlayerSettings();

            AssetDatabase.SaveAssets();
            if (created || interactive) Debug.Log("[ExcelHeroes] project setup ready — open Assets/ExcelHeroes/Scenes/Main.unity and press Play.");
        }

        static PanelSettings EnsurePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (existing != null) return existing;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null)
            {
                // The .tss has not been imported yet; try again once the import queue drains.
                EditorApplication.delayCall += () => Run(interactive: false);
                return null;
            }

            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.themeStyleSheet = theme;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1920);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;          // portrait phones vary in aspect; split the difference

            Directory.CreateDirectory(Path.GetDirectoryName(PanelPath)!);
            AssetDatabase.CreateAsset(panel, PanelPath);
            return panel;
        }

        static bool EnsureScene(PanelSettings panel)
        {
            if (File.Exists(ScenePath)) return false;

            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (uxml == null)
            {
                EditorApplication.delayCall += () => Run(interactive: false);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);

            // Additive is the polite mode — it leaves whatever the user has open alone — but Unity
            // refuses it while an unsaved untitled scene is active, which is exactly the state a
            // batchmode run starts in. In that case there is nothing to protect, so replace it.
            var active = EditorSceneManager.GetActiveScene();
            var additive = !string.IsNullOrEmpty(active.path);
            if (additive && active.isDirty && !Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                additive ? NewSceneMode.Additive : NewSceneMode.Single);

            var go = new GameObject("App");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = uxml;
            go.AddComponent<AppRoot>();

            EditorSceneManager.MoveGameObjectToScene(go, scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (additive) EditorSceneManager.CloseScene(scene, removeScene: true);
            AssetDatabase.ImportAsset(ScenePath);
            return true;
        }

        static void EnsureBuildSettings()
        {
            if (EditorBuildSettings.scenes.Any(s => s.path == ScenePath)) return;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) }
                .Concat(EditorBuildSettings.scenes.Where(s => s.path != ScenePath))
                .ToArray();
        }

        static void EnsurePlayerSettings()
        {
            PlayerSettings.companyName = "qugo";
            PlayerSettings.productName = "Excel Heroes";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
        }
    }
}
