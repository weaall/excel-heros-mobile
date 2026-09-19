using System.Collections;
using System.Linq;
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
    ///   ExcelHeroes.exe -screenshots &lt;dir&gt; [-screen-width 1080 -screen-height 486]
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

            /// <summary>
            /// Gives the capture run one card of each grade and puts them in the party, so the
            /// battle, the roster modal and the party sheet are photographed with content in them.
            /// In memory only: the save is never touched.
            /// </summary>
            static void SeedPartyForCapture()
            {
                var p = Game.Player;
                // Every save now starts with 김인턴 in it, so "has anyone" is no longer the same
                // question as "has anyone been recruited". Seed when the roster is only him.
                if (p == null) return;
                if (p.owned.Any(o => o.id != Data.GameData.MainId)) return;

                foreach (var grade in new[] { "S", "A", "B", "C", "D" })
                {
                    var def = Data.GameData.Heroes.Find(h => h.grade == grade);
                    if (def == null) continue;
                    p.owned.Add(new OwnedHero(def.id) { star = 2, level = 20 });
                    p.AddToParty(def.id);
                }
            }

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

                // Whatever a brand new save opens on, before anything is dismissed. On a fresh
                // profile that is the prologue, which is exactly the screen most likely to be wrong
                // and least likely to be looked at twice.
                yield return Shoot($"{n++:00}-Opening");
                app.CloseOverlay();
                yield return null;

                // The prologue, forced open. It only appears on a genuinely new save, so on any
                // machine that has run the game once it can never be checked by accident — which
                // is how it went unnoticed that the art was being cropped to a third of itself.
                // CloseOverlay rather than the screen's own Finish, so the save is not marked read.
                new PrologueScreen(app).Show();
                yield return null;
                yield return Shoot($"{n++:00}-Prologue");
                app.CloseOverlay();
                yield return null;

                // A save with nobody in it renders half these screens as empty states, which is a
                // real screen but not the one that needs checking. Seed a party in memory only —
                // Game.Touch is deliberately not called, so nothing here reaches the save file.
                SeedPartyForCapture();
                yield return null;

                foreach (AppRoot.Sheet sheet in System.Enum.GetValues(typeof(AppRoot.Sheet)))
                {
                    app.Show(sheet);
                    // The battle only positions its fighters once the sim has ticked, so a shot two
                    // frames in catches five sprites stacked on one another and says nothing about
                    // the layout. Everything else is static and needs no settling time.
                    if (sheet == AppRoot.Sheet.Home) yield return new WaitForSeconds(2.5f);
                    yield return Shoot($"{n:00}-{sheet}");
                    n++;

                    // The home sheet gets a second shot a few seconds later. The first one keeps
                    // landing on an EX cut-in, and a screen that is 40% covered by a portrait tells
                    // you nothing about the fight underneath it.
                    // A burst of frames rather than one. Combat effects live for a fifth of a
                    // second, so a single capture lands on a lull as often as on a hit and says
                    // nothing about whether the fight has any impact in it.
                    if (sheet == AppRoot.Sheet.Home)
                        for (var f = 0; f < 6; f++)
                        {
                            yield return new WaitForSeconds(0.55f);
                            yield return Shoot($"{n:00}-Fight{f}");
                        }
                }

                // The state that has been wrong and invisible: a card opened at full size. It is
                // an overlay over a screen, so it is captured in place.
                app.Show(AppRoot.Sheet.Roster);
                yield return null;
                var lead = Game.Player.owned.Count > 0 ? Game.Player.owned[0].id : null;
                if (lead != null)
                {
                    app.OpenDetail(lead, "info");
                    yield return Shoot($"{n++:00}-Detail");

                    // 스킨 is a pane of its own and a default open never reaches it.
                    app.OpenDetail(lead, "skin");
                    yield return Shoot($"{n++:00}-Skins");
                    app.OpenDetail(lead, "info");
                    app.CloseOverlay();
                }

                // 승진 belongs to one hero only, so it needs its own open rather than riding on
                // whichever card happens to be first in the roster.
                if (Game.Player.Owns(Data.GameData.MainId))
                {
                    app.OpenDetail(Data.GameData.MainId, "promo");
                    yield return Shoot($"{n++:00}-Promotion");
                    app.OpenDetail(Data.GameData.MainId, "info");
                    app.CloseOverlay();
                }

                Debug.Log($"[shots] wrote {n} screenshots to {Directory}");
                Application.Quit(0);
            }

            IEnumerator Shoot(string name)
            {
                yield return new WaitForEndOfFrame();

                var doc = FindFirstObjectByType<UnityEngine.UIElements.UIDocument>();
                if (doc != null)
                {
                    var problems = LayoutAudit.Run(doc.rootVisualElement);
                    if (problems.Count > 0)
                    {
                        Debug.LogWarning($"[shots] {name}: {problems.Count} layout problem(s)");
                        foreach (var p in problems) Debug.LogWarning($"[shots]   {p}");
                    }
                }

                var path = Path.Combine(Directory, name + ".png");
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Destroy(tex);
                yield return null;
            }
        }
    }
}
