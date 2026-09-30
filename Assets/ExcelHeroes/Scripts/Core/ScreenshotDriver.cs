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

            // a capture run is deterministic: game time advances 1/60 s a frame whatever the machine does, and the
            // dice are seeded — so two builds photograph the same moment of the same fight and an A/B between
            // them compares the change, not the luck of the frame (it had: a pitch change "won" the victory
            // close-up it does not touch, 6/6)
            Time.captureFramerate = 60;
            UI.LoadingScreen.Suppress = true;   // shot on its own (24-Loading), never over another screen
            Random.InitState(20260930);
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
                // A capture run builds its own state from nothing.
                //
                // This used to seed only when the roster was empty, which meant it seeded on the
                // first run of a machine and never again: every later capture photographed whatever
                // the previous run happened to leave in the save. Raising the seeded ★ did nothing,
                // and a screen's contents depended on the order someone had run things in. A
                // capture has to show the same thing every time or comparing two of them is
                // meaningless.
                //
                // Nothing is written back — the driver quits without ever marking the save dirty.
                Game.ReplaceState(PlayerState.New());
                var p = Game.Player;
                if (p == null) return;

                foreach (var grade in new[] { "S", "A", "B", "C", "D" })
                {
                    var def = Data.GameData.Heroes.Find(h => h.grade == grade);
                    if (def == null) continue;
                    // The S is taken to ★5 so the 각성 panel — which only exists at ★5 — is
                    // actually drawn. A panel gated behind a rank no seeded hero holds is a panel
                    // the driver silently never renders.
                    p.owned.Add(new OwnedHero(def.id)
                    {
                        star = grade == "S" ? 5 : 2,
                        level = 20,
                        skillLv = grade == "S" ? 2 : 0,
                        // Spare duplicates on the ★5, so 조각 변환 has something to offer; a ★5
                        // with none shows neither 스카우트 (it is maxed) nor 변환 (nothing spare),
                        // and the rows go unphotographed.
                        copies = grade == "S" ? 3 : 0,
                    });
                    p.AddToParty(def.id);

                    // One card marked 즐겨찾기, so the 보존 badge on the grid is something a
                    // capture can actually be checked against. A badge that only appears in a
                    // state the driver never builds is a badge nobody has ever seen.
                    if (grade == "A") p.favorites.Add(def.id);
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
                var prologue = new PrologueScreen(app);
                prologue.Show();
                yield return new WaitForSeconds(1.2f);
                yield return Shoot($"{n++:00}-Prologue");
                // the scene where the sheet appears, its last line typed and the sheet filled in
                prologue.Jump(4, 3);
                yield return new WaitForSeconds(1.2f);
                yield return Shoot($"{n++:00}-PrologueSheet");
                app.CloseOverlay();
                yield return null;

                // 입사 안내: the guide's first line, then the last step with the pointer on 모집.
                new Onboarding(app).Show(0);
                yield return new WaitForSeconds(0.5f);
                yield return Shoot($"{n++:00}-Guide");
                app.CloseOverlay();
                yield return null;
                new Onboarding(app).Show(3);
                yield return new WaitForSeconds(0.6f);
                yield return Shoot($"{n++:00}-GuidePoint");
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
                    // A new screen slides and fades in over 0.2s (Juice.Enter); a shot inside that
                    // window photographs an empty sheet.
                    else yield return new WaitForSeconds(0.4f);
                    yield return Shoot($"{n:00}-{sheet}");
                    n++;

                    // The battle's result lands over its HUD almost at once, so the HUD itself was
                    // never photographed. Take the result away and shoot the field underneath.
                    if (sheet == AppRoot.Sheet.Battle)
                    {
                        var docRoot = app.GetComponent<UnityEngine.UIElements.UIDocument>()?.rootVisualElement;
                        if (docRoot != null)
                            UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(docRoot, null, "bresult")
                                .ForEach(e => e.RemoveFromHierarchy());
                        // the result state also hides the HUD by class; lift it so the HUD shows
                        if (docRoot != null)
                            UnityEngine.UIElements.UQueryExtensions.Query<UnityEngine.UIElements.VisualElement>(docRoot, null, "battle--result")
                                .ForEach(e => e.RemoveFromClassList("battle--result"));
                        yield return new WaitForSeconds(0.3f);
                        yield return Shoot($"{n:00}-BattleHud");

                        // The fight itself: the next run starts on its own after the result, so
                        // wait it out and take a burst from the party's entrance onwards.
                        BattleSim.DebugTanky = 25;   // the capture party wipes a wave in a second: keep the monsters up to be seen
                        yield return new WaitForSeconds(2.9f);
                        // the frame cost of an ordinary fight, measured with no capture in the window
                        var probe = PerfProbe.Ensure();
                        probe.Begin("fight (5 heroes, a wave)");
                        yield return new WaitForSeconds(4f);
                        var perf = probe.End();
                        for (var f = 0; f < 6; f++)
                        {
                            yield return new WaitForSeconds(0.45f);
                            // a fight frame is a live fight: past a result, wait for the next run's squad to be at it
                            for (var w = 0f; BattleScreen.Current?.DebugFinished == true && w < 8f; w += 0.25f) yield return new WaitForSeconds(0.25f);
                            if (f == 0) BattleSim.DebugTanky = 25;
                            yield return Shoot($"{n:00}-Fight{f}");
                        }
                        // the EX cut-in, over the live fight (the UI's transitions run on real time; a capture runs faster)
                        BattleScreen.Current?.DebugCutIn();
                        yield return new WaitForSecondsRealtime(0.4f);
                        yield return Shoot($"{n:00}-CutIn");
                        // a win: the result over the party's close-up cheer (the BA cross-check's "result")
                        BattleScreen.Current?.DebugWin();
                        yield return new WaitForSeconds(2.4f);   // past the entry, into the held victory poses
                        yield return Shoot($"{n:00}-Win");
                        yield return new WaitForSeconds(4f);   // the next run starts on its own

                        // the battle's 메뉴, open over the live fight
                        BattleScreen.Current?.DebugMenu(true);
                        yield return new WaitForSeconds(0.3f);
                        yield return Shoot($"{n:00}-Menu");
                        BattleScreen.Current?.DebugMenu(false);
                        // The boss HUD (bar, layer badge, trail) and the hit rings only exist in a
                        // boss wave, which a capture pass never reaches on its own: bring it on.
                        Debug.Log("[shots] before boss: " + BattleScreen.Current?.DebugState());
                        BattleSim.DebugTanky = 0;
                        BattleScreen.Current?.DebugBoss(0.63f);
                        yield return new WaitForSeconds(0.6f);
                        probe.Begin("boss wave (strikes, EX)");
                        for (var hi = 0; hi < 5; hi++) BattleScreen.Current?.DebugSkill(hi);
                        ExcelHeroes.World.BattleWorld.Instance?.DebugTelegraph("stomp");
                        yield return new WaitForSeconds(2f);
                        perf += probe.End();
                        ExcelHeroes.World.BattleWorld.Instance?.DebugTelegraph("none");
                        File.WriteAllText(Path.Combine(Directory, "..", "perf.txt"), perf);
                        Debug.Log("[shots] at Boss0: " + BattleScreen.Current?.DebugState());
                        yield return Shoot($"{n:00}-Boss0");
                        yield return new WaitForSeconds(0.2f);
                        yield return Shoot($"{n:00}-Boss1");
                        // each boss telegraph shape, forced on the boss in the field (no EX cut-in over it)
                        BattleScreen.Current?.DebugAutoSkill(false);
                        foreach (var kind in new[] { "volley", "stomp", "throw", "sweep", "slow" })
                        {
                            if (ExcelHeroes.World.BattleWorld.Instance?.DebugTelegraph(kind) != true) break;
                            if (kind == "stomp")
                                for (var sf = 0; sf < 8; sf++) { yield return new WaitForSeconds(0.1f); yield return Shoot($"{n:00}-Stomp{sf}"); }
                            yield return new WaitForSeconds(0.35f);
                            yield return Shoot($"{n:00}-Tele-{kind}");
                            yield return new WaitForSeconds(0.4f);            // the strikes coming down (they land at 0.95 s)
                            yield return Shoot($"{n:00}-Tele-{kind}-b");
                            yield return new WaitForSeconds(0.3f);
                        }
                        ExcelHeroes.World.BattleWorld.Instance?.DebugTelegraph("none");
                        BattleScreen.Current?.DebugAutoSkill(true);
                        // every member's EX, one after another, against the boss (deep enough not to fall)
                        for (var hi = 0; hi < 5; hi++)
                        {
                            var st = BattleScreen.Current?.DebugSkill(hi);
                            Debug.Log($"[shots] skill {hi}: {st ?? "not fired"}");
                            if (st == null) continue;
                            yield return new WaitForSeconds(0.16f);
                            yield return Shoot($"{n:00}-Skill{hi}-{st}");
                            yield return new WaitForSeconds(0.6f);
                        }

                        // a member going down: the death motion, three frames of it and the body lying
                        if (BattleScreen.Current?.DebugDown(4) == true)
                        {
                            for (var df = 0; df < 4; df++) { yield return new WaitForSeconds(df == 3 ? 0.6f : 0.22f); yield return Shoot($"{n:00}-Down{df}"); }
                        }

                        // -burst: a run of close frames (24 × 0.07 s) to judge the motion in time —
                        // joint pops, parts coming loose, the springs settling — on a contact sheet
                        if (ArgValue("-burst") != null || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-burst") >= 0)
                        {
                            yield return new WaitForSeconds(3.2f);          // past the next run's entrance walk, into the fighting
                            for (var f = 0; f < 30; f++)
                            {
                                yield return new WaitForSeconds(0.07f);
                                yield return Shoot($"{n:00}-Burst{f:00}");
                            }
                        }
                    }

                    // The home sheet gets a second shot a few seconds later. The first one keeps
                    // landing on an EX cut-in, and a screen that is 40% covered by a portrait tells
                    // you nothing about the fight underneath it.
                    // A burst of frames rather than one. Combat effects live for a fifth of a
                    // second, so a single capture lands on a lull as often as on a hit and says
                    // nothing about whether the fight has any impact in it.

                }

                // The state that has been wrong and invisible: a card opened at full size. It is
                // an overlay over a screen, so it is captured in place.
                app.Show(AppRoot.Sheet.Roster);
                yield return null;
                // A RECRUITED hero, not owned[0] — which is 김인턴 now, and half of what 정보
                // shows does not apply to him: he uses no 조각, so 스카우트 and 변환 both return
                // without drawing. A capture that opens on him photographs their absence.
                var lead = Game.Player.owned.FirstOrDefault(o => o.id != Data.GameData.MainId && o.star < 5)?.id
                        ?? Game.Player.owned.FirstOrDefault(o => o.id != Data.GameData.MainId)?.id
                        ?? (Game.Player.owned.Count > 0 ? Game.Player.owned[0].id : null);
                if (lead != null)
                {
                    app.OpenDetail(lead, "info");
                    yield return Shoot($"{n++:00}-Detail");

                    // 스킨 is a pane of its own and a default open never reaches it.
                    // 강화 carries three separate panels — levelling, 스킬 레벨 and 각성 — and
                    // none of them is on the tab a detail sheet opens on.
                    // 강화 opens on the ★5 card on purpose: 각성 only exists at ★5, and `lead`
                    // is 김인턴, who is never in the recruit pool and so can never be given the
                    // duplicates a ★ costs.
                    var five = Game.Player.owned.FirstOrDefault(o => o.star >= 5)?.id ?? lead;
                    app.OpenDetail(five, "power");
                    yield return Shoot($"{n++:00}-Enhance");

                    // And again on a card below ★5, because 스카우트 only exists there while
                    // 각성 only exists at ★5 — one hero can never show both.
                    var low = Game.Player.owned.FirstOrDefault(
                        o => o.id != Data.GameData.MainId && o.star < 5)?.id;
                    if (low != null)
                    {
                        app.OpenDetail(low, "power");
                        yield return Shoot($"{n++:00}-Scout");
                    }

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

                // The ten-pull result, which only exists after spending gems.
                app.OpenOverlay(GachaScreen.Sample(app.CloseOverlay));
                yield return null;
                yield return null;
                yield return Shoot($"{n++:00}-Pull10");
                app.CloseOverlay();
                // the entrance of an S, the member's card over the result, and the 일반 banner
                app.OpenOverlay(GachaScreen.SampleIntro());
                yield return new WaitForSeconds(0.7f);
                yield return Shoot($"{n - 1:00}-PullIntro");
                app.OpenOverlay(GachaScreen.SampleInfo(app.CloseOverlay));
                yield return new WaitForSeconds(0.4f);
                yield return Shoot($"{n - 1:00}-PullInfo");
                app.CloseOverlay();
                app.Show(AppRoot.Sheet.Gacha);
                yield return new WaitForSeconds(0.4f);
                GachaScreen.Current?.DebugBanner(true);
                yield return null; yield return null;
                yield return Shoot($"{n - 1:00}-GachaNormal");
                GachaScreen.Current?.DebugBanner(false);

                // 설정, each tab
                for (var st = 0; st < 3; st++)
                {
                    app.OpenSettings(st);
                    yield return new WaitForSeconds(0.3f);
                    yield return Shoot($"{n - 1:00}-Settings{st}");
                }
                app.CloseOverlay();

                app.Show(AppRoot.Sheet.Home);
                UI.InboxPanels.OpenMail(app, null);
                yield return null; yield return null;
                yield return Shoot($"{n++:00}-Mail");
                app.CloseOverlay();
                app.ShowIdle(IdleService.For(Game.Player, 8 * 3600 + 30 * 60));
                yield return null; yield return null;
                yield return Shoot($"{n++:00}-Idle");
                app.CloseOverlay();
                app.SetStatus("한꺼번에 수령 3건: 보석 +80 · 골드 +1,200");
                yield return new WaitForSeconds(0.5f);
                yield return Shoot($"{n++:00}-Toast");
                app.CloseOverlay();
                UI.InboxPanels.OpenNotice(app, null);
                yield return null; yield return null;
                yield return Shoot($"{n++:00}-Notice");
                app.CloseOverlay();
                var lp = UI.LoadingScreen.Show(FindFirstObjectByType<UnityEngine.UIElements.UIDocument>()?.rootVisualElement, 1000f);
                yield return new WaitForSeconds(0.8f);
                yield return Shoot($"{n++:00}-Loading");
                lp?.RemoveFromHierarchy();

                app.OpenOverlay(UiGallery.BuildSheets(app.CloseOverlay));
                yield return null;
                yield return null;
                yield return Shoot($"{n++:00}-Sheets");
                app.CloseOverlay();

                // Every plate and the modal on one page, laid out like the reference sheet, so
                // the two can be compared side by side. See UiGallery.
                app.OpenOverlay(UiGallery.Build(app.CloseOverlay));
                yield return null;
                yield return null;
                yield return Shoot($"{n++:00}-Kit");
                app.CloseOverlay();

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
                // -shotsuntil <part>: stop after the first shot whose name contains it (A/B variant captures)
                var until = ArgValue("-shotsuntil");
                if (!string.IsNullOrEmpty(until) && name.Contains(until)) Application.Quit();
                yield return null;
            }
        }
    }
}
