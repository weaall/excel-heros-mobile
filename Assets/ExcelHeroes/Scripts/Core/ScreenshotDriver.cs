using System.Collections;
using System.IO;
using ExcelHeroes.UI;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// Walks the built player through every sheet and writes a PNG of each, then quits.
    ///
    /// This exists because the game shipped once completely blank and the only way anyone found out
    /// was by asking a person to look at it. Eighty logic checks passed against an empty screen.
    /// A layout is not verifiable from the outside, so the build verifies itself:
    ///
    ///   ExcelHeroes.exe -screenshots &lt;dir&gt; [-screen-width 540 -screen-height 960]
    ///
    /// It captures the real player, not an editor preview, so what lands in the folder is exactly
    /// what a player sees — wrong fonts, clipped panels, unstyled overlays and all.
    /// </summary>
    public static class ScreenshotDriver
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var dir = ArgValue("-screenshots");
            if (string.IsNullOrEmpty(dir)) return;

            var go = new GameObject("ScreenshotDriver");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Directory = dir;
        }

        static string ArgValue(string flag)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == flag) return args[i + 1];
            return null;
        }

        class Runner : MonoBehaviour
        {
            public string Directory;

            IEnumerator Start()
            {
                System.IO.Directory.CreateDirectory(Directory);

                // UI Toolkit lays out on the frame after the tree is built, and the first frame of a
                // fresh player is also the one that loads the save; two frames of slack keeps the
                // first shot from catching a half-built sheet.
                yield return null;
                yield return null;

                var app = FindFirstObjectByType<AppRoot>();
                if (app == null)
                {
                    Debug.LogError("[shots] no AppRoot in the scene — nothing to capture");
                    Application.Quit(1);
                    yield break;
                }

                var n = 0;
                foreach (AppRoot.Sheet sheet in System.Enum.GetValues(typeof(AppRoot.Sheet)))
                {
                    app.Show(sheet);
                    // The battle only positions its fighters once the sim has ticked, so a shot two
                    // frames in catches five sprites stacked on one another and says nothing about
                    // the layout. Everything else is static and needs no settling time.
                    if (sheet == AppRoot.Sheet.Home) yield return new WaitForSeconds(2.5f);
                    yield return Shoot($"{n:00}-{sheet}");
                    n++;
                }

                // The two states that have been wrong and invisible: a card opened full size, and
                // the disguise. Both are overlays over a sheet, so they are captured in place.
                app.Show(AppRoot.Sheet.Roster);
                yield return null;
                var lead = Game.Player.owned.Count > 0 ? Game.Player.owned[0].id : null;
                if (lead != null)
                {
                    app.OpenDetail(lead);
                    yield return Shoot($"{n++:00}-Detail");
                    app.CloseOverlay();
                }

                app.SetStealth(true);
                yield return Shoot($"{n++:00}-Stealth");
                app.SetStealth(false);

                Debug.Log($"[shots] wrote {n} screenshots to {Directory}");
                Application.Quit(0);
            }

            IEnumerator Shoot(string name)
            {
                yield return new WaitForEndOfFrame();
                var path = Path.Combine(Directory, name + ".png");
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                yield return null;
            }
        }
    }
}
