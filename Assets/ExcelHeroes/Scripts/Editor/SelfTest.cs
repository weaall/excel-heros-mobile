using System;
using System.Linq;
using System.Text;
using ExcelHeroes.Core;
using ExcelHeroes.Data;
using UnityEditor;
using UnityEngine;

namespace ExcelHeroes.EditorTools
{
    /// <summary>
    /// Headless checks for the parts of the game that are pure logic: the data actually loads, the
    /// gacha honours its published rates and never overshoots a pity floor, stats move the way the
    /// balance table says, and a battle can be played to a conclusion.
    ///
    /// Runs from the menu, or headlessly:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod ExcelHeroes.EditorTools.SelfTest.Run
    /// Failures are logged as errors, which makes the batch run exit non-zero.
    /// </summary>
    public static class SelfTest
    {
        static int _failures;
        static readonly StringBuilder Log = new();

        /// <summary>
        /// Plays real battles across a stage range and reports how often a party of a given strength
        /// actually wins. The point is to tune the curve against measurements rather than against a
        /// feeling — a stage the intended party wins 100% of the time is not a stage, and one it wins
        /// 0% of the time is a wall the player cannot read.
        /// </summary>
        [MenuItem("Excel Heroes/Run Balance Bench")]
        public static void Bench()
        {
            GameData.Load();
            var report = new StringBuilder();
            report.AppendLine("=== balance bench: win rate by stage and party strength ===");
            // "timeout" is tracked apart from "loss" on purpose: a loss is the fight being too hard,
            // a timeout is the fight not resolving, and only the second one is a bug.
            report.AppendLine("party            stage  win%  timeout  avg time  avg gold");

            // Rows model what a player plausibly HAS at that point, not an abstract tier: a starter
            // party is ★1 level 1, but by stage 5 they have spent a few battles' gold on levels.
            foreach (var (label, star, level, grades) in new[]
            {
                ("day1 D/C ★1 L1",   1, 1,  new[] { "D", "C" }),
                ("day1 levelled L20",1, 20, new[] { "D", "C" }),
                ("mid B/A ★3 L40",   3, 40, new[] { "B", "A" }),
                ("built A/S ★5 L90", 5, 90, new[] { "A", "S" }),
            })
            {
                foreach (var stage in new[] { 1, 3, 5, 10, 20, 40, 60 })
                {
                    var wins = 0; var timeouts = 0; var time = 0f; var gold = 0L;
                    const int runs = 40;
                    for (var i = 0; i < runs; i++)
                    {
                        var p = PartyOf(grades, star, level);
                        var sim = new BattleSim(p, stage) { AutoSkill = true };
                        var guard = 0;
                        // The sim stops itself at BattleSim.TimeLimit; the step guard is only there so
                        // a future bug cannot hang the Editor.
                        while (!sim.Finished && guard < 60 * 600) { sim.Tick(1f / 60f); sim.Events.Clear(); guard++; }
                        if (sim.Won) wins++;
                        if (sim.TimedOut) timeouts++;
                        time += sim.Elapsed;
                        gold += sim.GoldEarned;
                    }
                    report.AppendLine($"{label,-16} {stage,5}  {wins * 100 / runs,3}%  {timeouts,6}  {time / runs,7:F1}s  {gold / runs,9:N0}");
                }
                report.AppendLine();
            }

            Debug.Log(report.ToString());
        }

        static PlayerState PartyOf(string[] grades, int star, int level)
        {
            var p = PlayerState.New();
            // One of each role where possible, so the bench measures the curve rather than a
            // pathological all-healer or all-tank line-up.
            foreach (var role in new[] { "tank", "melee", "ranged", "healer", "melee" })
            {
                var pick = GameData.Heroes.FirstOrDefault(h =>
                    h.role == role && grades.Contains(h.grade) && !p.Owns(h.id));
                pick ??= GameData.Heroes.FirstOrDefault(h => grades.Contains(h.grade) && !p.Owns(h.id));
                if (pick == null) continue;
                p.owned.Add(new OwnedHero(pick.id) { star = star, level = level });
                p.AddToParty(pick.id);
            }
            return p;
        }

        [MenuItem("Excel Heroes/Run Self Test")]
        public static void Run()
        {
            _failures = 0;
            Log.Clear();
            Line("=== Excel Heroes self test ===");

            GameData.Load();
            CheckSceneWiring();
            CheckData();
            CheckGachaRates();
            CheckPity();
            CheckStats();
            CheckSynergy();
            CheckPickup();
            CheckAffection();
            CheckQuests();
            CheckBattle();
            CheckGatesAreReachable();
            CheckSaveTransfer();

            Line(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
            if (_failures == 0) Debug.Log(Log.ToString());
            else Debug.LogError(Log.ToString());
        }

        /// <summary>
        /// 세이브 이동 — a round trip, and the ways a bad string can arrive.
        ///
        /// None of this is visible in a screenshot: an export that silently drops a field and an
        /// import that silently half-loads both look like a working button. With cloud sync blocked
        /// this is the only way a save leaves a device, so losing a roster here loses it for good.
        /// </summary>
        static void CheckSaveTransfer()
        {
            Line("\n-- save transfer --");

            var before = PlayerState.New();
            before.gold = 12345;
            before.gems = 678;
            before.cards = 90;
            before.stage = 42;
            before.maxCleared = 41;
            before.mainJob = "staff";
            var hero = GameData.Heroes.Count > 0 ? GameData.Heroes[0].id : null;
            if (hero != null)
                before.owned.Add(new OwnedHero(hero) { star = 3, level = 77, copies = 5, awakened = true, skillLv = 2 });

            var code = SaveService.Export(before);
            Check(code.Length > 0, $"export produces a code ({code.Length} chars)");

            var round = SaveService.Import(code);
            Check(round.Ok, "a code this build wrote imports back" + (round.Ok ? "" : ": " + round.Error));
            if (!round.Ok) return;

            var after = round.State;
            Check(after.gold == before.gold && after.gems == before.gems && after.cards == before.cards,
                  "currencies survive the round trip");
            Check(after.stage == before.stage && after.maxCleared == before.maxCleared,
                  "progress survives the round trip");
            Check(after.mainJob == before.mainJob, $"승진 survives ({after.mainJob})");

            if (hero != null)
            {
                var h = after.Find(hero);
                Check(h != null && h.star == 3 && h.level == 77 && h.copies == 5
                      && h.awakened && h.skillLv == 2,
                      "a hero keeps ★, level, 중복, 각성 and 스킬 레벨");
            }

            // The ways a bad string arrives. Each has to fail with a message, never with a
            // half-loaded save — an import that half-works destroys the device's real one.
            Check(!SaveService.Import(null).Ok, "null is refused");
            Check(!SaveService.Import("   ").Ok, "blank is refused");
            Check(!SaveService.Import("not base64 at all !!").Ok, "junk is refused");
            Check(!SaveService.Import(System.Convert.ToBase64String(
                      System.Text.Encoding.UTF8.GetBytes("{\"hello\":1}"))).Ok,
                  "valid base64 that is not a save is refused");
            Check(!SaveService.Import(code.Substring(0, code.Length / 2)).Ok,
                  "a truncated code is refused");
        }

        /// <summary>
        /// Every gate ported from the web, checked against THIS build's ceilings.
        ///
        /// This exists because of a real bug that shipped. 과장 → 부장 requires level 140, and the
        /// main hero was capped by ★ like everyone else — but ★ is bought with duplicates and he is
        /// never in the recruit pool, so he is ★1 forever and stopped at 80. The last promotion was
        /// unreachable. Every screen rendered correctly, every other check passed, and the wall was
        /// forty hours into a save.
        ///
        /// The two builds do not level the same way, so a requirement copied across can be
        /// satisfiable there and impossible here. That is arithmetic, and arithmetic is cheap to
        /// check, so it is checked on every run rather than remembered.
        /// </summary>
        static void CheckGatesAreReachable()
        {
            Line("\n-- gates are reachable --");
            var b = GameData.Balance;

            // 승진: each tier's level requirement against the ceiling that tier actually opens.
            var need = b.mainPromoteLevel;
            var caps = b.mainLevelCapByTier;
            if (need == null || caps == null || need.Length == 0 || caps.Length == 0)
                Check(false, "승진 tables exported (mainPromoteLevel / mainLevelCapByTier)");
            else
                for (var tier = 0; tier < need.Length; tier++)
                {
                    var cap = caps[System.Math.Min(tier, caps.Length - 1)];
                    Check(need[tier] <= cap,
                          $"승진 tier {tier}: needs level {need[tier]}, tier caps at {cap}");
                }

            // 스킨: an affection unlock past the affection ceiling can never be earned, and the
            // skin would sit in the panel forever saying what to do about it.
            foreach (var sk in GameData.SkinDefs)
            {
                if (sk.unlockAffection <= 0) continue;
                Check(sk.unlockAffection <= b.affectionMax,
                      $"스킨 {sk.heroDefId}/{sk.id}: needs 호감도 Lv {sk.unlockAffection}, max is {b.affectionMax}");
            }

            // A skin has to have art baked for it, or equipping it silently shows the base look.
            var missingSkinArt = 0;
            foreach (var sk in GameData.SkinDefs)
                if (Resources.Load<Sprite>($"Art/Cards/{sk.heroDefId}__{sk.id}") == null) missingSkinArt++;
            Check(missingSkinArt == 0, $"every skin has an illustration ({GameData.SkinDefs.Count} skins, {missingSkinArt} missing)");

            // 비품 upgrades cannot be asked to go past their own maximum.
            Check(b.equipMaxLevel > 0, $"비품 upgrade ceiling is set ({b.equipMaxLevel})");

            // The main hero must have a doll under every job he can hold, or he fights as a
            // circle of card art the moment he is promoted into one that has none.
            var missingJobArt = 0;
            foreach (var job in GameData.MainJobs)
                if (Resources.Load<Sprite>($"Art/Sprites/{job.id}") == null) missingJobArt++;
            Check(missingJobArt == 0, $"every job has a battle sprite ({GameData.MainJobs.Count} jobs, {missingJobArt} missing)");
        }

        static void Line(string s) => Log.AppendLine(s);

        static void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Line($"  [{(ok ? "ok" : "FAIL")}] {what}");
        }

        /// <summary>
        /// The wiring, checked against the scene file rather than against the API.
        ///
        /// This exists because the game shipped once with UIDocument.panelSettings null. It ran, it
        /// logged, it threw nothing, every one of the eighty logic checks passed — and the screen was
        /// blank, because a UIDocument without PanelSettings silently renders nothing. Correct logic
        /// with a disconnected wire is not a game, and nothing else here was looking at the wires.
        ///
        /// It reads the serialised scene because that is what failed: the assignment appeared to
        /// succeed in code and did not survive the save.
        /// </summary>
        static void CheckSceneWiring()
        {
            Line("-- scene wiring --");
            const string scenePath = "Assets/ExcelHeroes/Scenes/Main.unity";
            const string panelPath = "Assets/ExcelHeroes/UI/PanelSettings.asset";
            const string uxmlPath = "Assets/ExcelHeroes/UI/AppShell.uxml";

            Check(System.IO.File.Exists(scenePath), "Main.unity exists");
            if (!System.IO.File.Exists(scenePath)) return;

            var scene = System.IO.File.ReadAllText(scenePath);
            var panelGuid = AssetDatabase.AssetPathToGUID(panelPath);
            var uxmlGuid = AssetDatabase.AssetPathToGUID(uxmlPath);

            Check(!string.IsNullOrEmpty(panelGuid), "PanelSettings asset exists");
            Check(!string.IsNullOrEmpty(uxmlGuid), "AppShell.uxml exists");

            // The two references that decide whether anything renders at all.
            Check(!scene.Contains("m_PanelSettings: {fileID: 0}"),
                "UIDocument.panelSettings is assigned (null here means a blank screen, with no error)");
            Check(scene.Contains(panelGuid), "the scene points at OUR PanelSettings");
            Check(scene.Contains(uxmlGuid), "the scene points at OUR AppShell.uxml");

            var panel = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(panelPath);
            Check(panel != null && panel.themeStyleSheet != null,
                "PanelSettings has a theme (without one, controls draw no text)");
            Check(panel != null && panel.targetTexture == null,
                "PanelSettings draws to the screen, not to a render texture");

            Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == scenePath),
                "Main.unity is enabled in Build Settings");

            // The shell needs these names; the screens query them and would take a null otherwise.
            var uxml = System.IO.File.Exists(uxmlPath) ? System.IO.File.ReadAllText(uxmlPath) : "";
            foreach (var name in new[] { "content", "overlay", "navbar", "statusText", "gemValue", "goldValue" })
                Check(uxml.Contains($"name=\"{name}\""), $"AppShell defines #{name}");
        }

        static void CheckData()
        {
            Line("\n-- data --");
            Check(GameData.Heroes.Count > 0, $"roster loaded ({GameData.Heroes.Count} heroes)");
            Check(GameData.Grades.Count == 5, $"5 grades ({string.Join(",", GameData.Grades.Select(g => g.id))})");
            Check(GameData.Roles.Count == 4, $"4 roles");
            Check(GameData.Episodes.Count > 0, $"{GameData.Episodes.Count} story episodes");
            Check(GameData.Balance != null && GameData.Balance.partySize > 0, "balance loaded");
            Check(GameData.MonsterTypes.Count > 0, $"{GameData.MonsterTypes.Count} monster types");
            Check(GameData.Bosses.Count > 0, $"{GameData.Bosses.Count} bosses");
            Check(GameData.Bosses.All(b => b.specials.Count > 0), "every boss has at least one scripted move");
            Check(GameData.BossForStage(1)?.id != GameData.BossForStage(11)?.id,
                $"boss rotates per phase (1: {GameData.BossForStage(1)?.name}, 11: {GameData.BossForStage(11)?.name})");

            var rateSum = GameData.Grades.Sum(g => g.rate);
            Check(Mathf.Abs(rateSum - 1f) < 0.02f, $"gacha rates sum to ~1 (got {rateSum:F3})");

            var missingArt = GameData.Heroes.Count(h => GameData.CardArt(h.id) == null);
            Check(missingArt == 0, $"card art present for every hero (missing {missingArt})");

            var orphanDivision = GameData.Heroes.Count(h => GameData.Division(h.division) == null);
            Check(orphanDivision == 0, $"every hero maps to a known division (bad {orphanDivision})");

            var orphanSkill = GameData.Heroes.Count(h => GameData.Skill(h.skillType) == null);
            Check(orphanSkill == 0, $"every hero maps to a known skill (bad {orphanSkill})");
        }

        static void CheckGachaRates()
        {
            Line("\n-- gacha distribution (200k pulls) --");
            const int n = 200000;
            var p = PlayerState.New();
            var counts = GameData.Grades.ToDictionary(g => g.id, _ => 0);

            for (var i = 0; i < n; i++) counts[GachaService.Pull(p).grade]++;

            foreach (var g in GameData.Grades)
            {
                var actual = counts[g.id] / (float)n;
                // The pity floors deliberately push A and S above their raw rate, so only assert a
                // floor on those two and a tight band on the rest.
                var top = g.id == "A" || g.id == "S";
                var ok = top ? actual >= g.rate * 0.9f : Mathf.Abs(actual - g.rate) < 0.02f;
                Check(ok, $"{g.id}: published {g.rate:P2}, rolled {actual:P2}");
            }
        }

        static void CheckPity()
        {
            Line("\n-- pity floors --");
            var b = GameData.Balance;
            var p = PlayerState.New();
            var worstA = 0; var worstS = 0; var sinceA = 0; var sinceS = 0;

            for (var i = 0; i < 100000; i++)
            {
                var r = GachaService.Pull(p);
                sinceA++; sinceS++;
                if (GameData.GradeRank(r.grade) >= GameData.GradeRank("A")) { worstA = Math.Max(worstA, sinceA); sinceA = 0; }
                if (r.grade == "S") { worstS = Math.Max(worstS, sinceS); sinceS = 0; }
            }

            Check(worstA <= b.pityA, $"longest A drought {worstA} <= floor {b.pityA}");
            Check(worstS <= b.pityS, $"longest S drought {worstS} <= floor {b.pityS}");
        }

        static void CheckStats()
        {
            Line("\n-- stats --");
            var def = GameData.Heroes.First(h => h.grade == "A");
            // Levelled up first: attack is floored to an int, and at base values that rounding is a
            // whole 5% of the number, which would swamp the ratio this check is actually about.
            var o = new OwnedHero(def.id) { level = 60 };

            var atk1 = StatMath.Atk(o);
            o.star = 5;
            var atk5 = StatMath.Atk(o);
            Check(atk5 > atk1, $"★5 beats ★1 ({atk1} -> {atk5})");

            var expected = StatMath.StarMult(5) / StatMath.StarMult(1);
            var ratio = atk5 / (float)atk1;
            Check(Mathf.Abs(ratio - expected) < 0.01f, $"★ multiplier matches table (x{ratio:F3} vs x{expected:F3})");

            o.star = 1;
            Check(!StatMath.SkillUnlocked(o), "skill locked at ★1");
            o.star = GameData.Balance.skillUnlockStar;
            Check(StatMath.SkillUnlocked(o), $"skill unlocks at ★{GameData.Balance.skillUnlockStar}");

            var low = new OwnedHero(def.id) { level = 10 };
            var high = new OwnedHero(def.id) { level = 40 };
            Check(StatMath.Atk(high) > StatMath.Atk(low),
                $"level raises attack (Lv10 {StatMath.Atk(low)} -> Lv40 {StatMath.Atk(high)})");
        }

        static void CheckSynergy()
        {
            Line("\n-- synergy --");
            var p = PlayerState.New();
            var tech = GameData.Heroes.Where(h => h.division == "tech").Take(3).ToList();
            Check(tech.Count == 3, "found 3 tech heroes to test with");
            if (tech.Count < 3) return;

            foreach (var h in tech) { p.owned.Add(new OwnedHero(h.id)); p.AddToParty(h.id); }
            var syn = StatMath.Synergy(p);
            Check(syn.atkBonus > 0f, $"3 same-division heroes grant attack (+{syn.atkBonus:P0})");
            Check(syn.perks.ContainsKey("cooldown"), "tech perk (cooldown) unlocked");

            var solo = PlayerState.New();
            solo.owned.Add(new OwnedHero(tech[0].id));
            solo.AddToParty(tech[0].id);
            Check(StatMath.Synergy(solo).atkBonus == 0f, "a lone hero grants no synergy");
        }

        static void CheckPickup()
        {
            Line("\n-- pickup banner --");
            Check(GameData.Pickup != null, "pickup data loaded");
            if (GameData.Pickup == null) return;

            var today = DateTime.UtcNow;
            var s = GameData.Featured("S", today);
            var a = GameData.Featured("A", today);
            Check(s != null && s.grade == "S", $"today's S pickup is an S ({s?.name})");
            Check(a != null && a.grade == "A", $"today's A pickup is an A ({a?.name})");

            var left = GameData.BannerDaysLeft(today);
            Check(left >= 1 && left <= GameData.Pickup.days, $"banner has {left} day(s) left");

            // Every card in a grade must get its turn, or a player chasing one of them waits forever.
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < 60; i++) seen.Add(GameData.Featured("S", today.AddDays(i * GameData.Pickup.days))?.id);
            var sCount = GameData.OfGrade("S").Count();
            Check(seen.Count >= sCount, $"every S gets a banner within 60 rotations ({seen.Count}/{sCount})");

            // Consecutive banners must differ, otherwise "come back on her day" means nothing.
            var repeats = 0;
            for (var i = 1; i < 30; i++)
                if (GameData.Featured("S", today.AddDays(i * GameData.Pickup.days))?.id ==
                    GameData.Featured("S", today.AddDays((i - 1) * GameData.Pickup.days))?.id) repeats++;
            Check(repeats == 0, $"no back-to-back repeat of the same S ({repeats})");

            // Rate-up must actually bias pulls towards the featured card.
            var p = PlayerState.New();
            int sTotal = 0, sFeatured = 0;
            for (var i = 0; i < 300000; i++)
            {
                var r = GachaService.Pull(p);
                if (r.grade != "S") continue;
                sTotal++;
                if (r.hero.id == s.id) sFeatured++;
            }
            var share = sTotal == 0 ? 0f : sFeatured / (float)sTotal;
            Check(share > 0.4f, $"featured S takes {share:P0} of S pulls (rate-up {GameData.Pickup.rate:P0})");

            // 모집 포인트 must accrue and buy the card outright.
            var spark = PlayerState.New();
            for (var i = 0; i < GameData.SparkCost("S"); i++) GachaService.Pull(spark);
            Check(GachaService.CanSpark(spark, "S"), $"{GameData.SparkCost("S")} pulls affords an S spark");
            var before = spark.sparkPoints;
            var got = GachaService.Spark(spark, "S");
            Check(got != null && got.hero.id == s.id, "spark hands over today's featured S");
            Check(spark.sparkPoints == before - GameData.SparkCost("S"), "spark charges the advertised price");
        }

        static void CheckAffection()
        {
            Line("\n-- affection --");
            var b = GameData.Balance;
            var def = GameData.Heroes.First(h => GameData.Affection(h.id) != null);
            var o = new OwnedHero(def.id);

            Check(o.affection == 0, "a new card starts at Lv0");
            Check(AffectionService.XpForNext(0) > 0, $"Lv1 costs {AffectionService.XpForNext(0)} xp");

            AffectionService.AddXp(o, 10000);
            Check(o.affection == b.affectionMax, $"enough xp reaches the cap (Lv{o.affection})");
            Check(AffectionService.AtMax(o) && o.affectionXp == 0, "capped cards stop banking xp");

            // Levelled first: at base stats a D card attacks for 6, and +10% of 6 floors straight
            // back to 6. The bonus is real, just invisible at integer scale.
            var fresh = new OwnedHero(def.id) { level = 40 };
            var atk0 = StatMath.Atk(fresh);
            fresh.affection = b.affectionMax;
            var atkMax = StatMath.Atk(fresh);
            Check(atkMax > atk0, $"affection raises attack ({atk0} -> {atkMax})");
            var expected = 1f + b.affectionMax * b.affectionBonusPerLevel;
            Check(Mathf.Abs(atkMax / (float)atk0 - expected) < 0.01f,
                $"the bonus matches the table (x{atkMax / (float)atk0:F3} vs x{expected:F3})");

            var mid = new OwnedHero(def.id) { affection = b.affectionUnlockSecret };
            Check(AffectionService.SecretUnlocked(mid), $"secret unlocks at Lv{b.affectionUnlockSecret}");
            Check(!AffectionService.LineUnlocked(mid), $"private message still locked at Lv{mid.affection}");
            mid.affection = b.affectionUnlockLine;
            Check(AffectionService.LineUnlocked(mid), $"private message unlocks at Lv{b.affectionUnlockLine}");
            Check(AffectionService.Greeting(mid) == GameData.Affection(def.id).line2,
                "the home greeting switches to the private line once known");

            // A gift must cost gold and be refused when broke.
            var p = PlayerState.New();
            var owned = new OwnedHero(def.id);
            p.owned.Add(owned); p.AddToParty(def.id);
            Check(AffectionService.Gift(p, owned) == -1, "a broke player cannot buy a snack");
            p.gold = AffectionService.GiftCost(p) * 2;
            var goldBefore = p.gold;
            AffectionService.Gift(p, owned);
            Check(p.gold < goldBefore, "a snack costs gold");
            Check(owned.affectionXp > 0 || owned.affection > 0, "a snack raises the bond");

            var missing = GameData.Heroes.Count(h => GameData.Affection(h.id) == null);
            Check(missing == 0, $"every hero has affection text (missing {missing})");
        }

        static void CheckQuests()
        {
            Line("\n-- daily quests --");
            var file = GameData.Quests;
            Check(file != null && file.items.Count > 0, $"{file?.items.Count ?? 0} quests defined");
            if (file == null) return;

            var today = QuestService.TodayKey();
            var ids = QuestService.TodayQuestIds(today);
            Check(ids.Count == file.perDay, $"today runs {ids.Count} of {file.items.Count} quests");
            Check(ids.Contains("kills"), "오류 처리 is always on the list");
            Check(ids.Distinct().Count() == ids.Count, "no quest appears twice in a day");
            Check(ids.All(id => GameData.Quest(id) != null), "every picked quest resolves to a definition");

            // Same day must give the same list, or a restart would reroll the dailies.
            Check(QuestService.TodayQuestIds(today).SequenceEqual(ids), "the list is stable within a day");

            // Over time every quest must get used, otherwise some are dead content.
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var i = 0; i < 120; i++)
                foreach (var id in QuestService.TodayQuestIds(DateTime.Now.AddDays(i).ToString("yyyy-MM-dd")))
                    seen.Add(id);
            Check(seen.Count == file.items.Count, $"every quest appears within 120 days ({seen.Count}/{file.items.Count})");

            // Progress, claiming, and the refusal to double-claim.
            var p = PlayerState.New();
            QuestService.EnsureToday(p);
            var def = GameData.Quest("kills");
            Check(!QuestService.CanClaim(p, "kills"), "an untouched quest cannot be claimed");

            QuestService.Note(p, "kills", def.target);
            Check(QuestService.CanClaim(p, "kills"), "hitting the target makes it claimable");

            var gemsBefore = p.gems;
            var (gems, _) = QuestService.Claim(p, "kills");
            Check(gems == def.gems && p.gems == gemsBefore + def.gems, $"claiming pays the advertised {def.gems} gems");
            Check(!QuestService.CanClaim(p, "kills"), "a claimed quest cannot be claimed again");

            // Progress must not overflow past the target — the bar would read wrong.
            var q = p.quests.First(x => x.id == "kills");
            Check(q.count <= def.target, $"progress is capped at the target ({q.count}/{def.target})");

            // Check-in and the streak.
            var s = PlayerState.New();
            Check(QuestService.CanCheckIn(s), "a fresh player can check in");
            var (inGems, _) = QuestService.CheckIn(s);
            Check(inGems >= file.loginGems, $"check-in pays at least the base {file.loginGems}");
            Check(s.streak == 1, "first check-in starts the streak at 1");
            Check(!QuestService.CanCheckIn(s), "cannot check in twice in a day");

            // A gap must reset the streak — that is what makes it worth protecting.
            s.checkInDate = DateTime.Now.AddDays(-3).ToString("yyyy-MM-dd");
            s.streak = 5;
            QuestService.CheckIn(s);
            Check(s.streak == 1, "a missed day resets the streak");

            // The all-clear bonus only after everything else.
            var a = PlayerState.New();
            QuestService.EnsureToday(a);
            Check(!QuestService.CanClaimAllClear(a), "all-clear is locked until every quest is claimed");
            foreach (var pq in a.quests)
            {
                QuestService.Note(a, pq.id, GameData.Quest(pq.id).target);
                QuestService.Claim(a, pq.id);
            }
            Check(QuestService.CanClaimAllClear(a), "all-clear unlocks once every quest is claimed");
            Check(QuestService.ClaimAllClear(a) == file.allClearGems, $"all-clear pays {file.allClearGems}");
            Check(!QuestService.CanClaimAllClear(a), "all-clear cannot be claimed twice");

            // The economy this exists to fix: a day's gems against the price of a ten-pull.
            var dayCeiling = file.loginGems + file.streakMaxDays * file.streakGemsPerDay + file.allClearGems
                             + ids.Sum(id => GameData.Quest(id).gems);
            var ten = GameData.Balance.gachaTenCost;
            Check(dayCeiling * 4 >= ten, $"a day's gems ({dayCeiling}) reach a ten-pull ({ten}) inside a week");
        }

        static void CheckBattle()
        {
            Line("\n-- battle --");
            var p = PlayerState.New();
            // A deliberately strong party, so the check is "a battle resolves", not "this party wins".
            foreach (var h in GameData.Heroes.OrderByDescending(h => GameData.GradeRank(h.grade)).Take(5))
            {
                p.owned.Add(new OwnedHero(h.id) { star = 5, level = 40 });
                p.AddToParty(h.id);
            }

            var sim = new BattleSim(p, stage: 1) { AutoSkill = true };
            Check(sim.Heroes.Count == 5, $"party fielded ({sim.Heroes.Count})");
            Check(sim.Monsters.Count > 0, "first wave spawned");

            var steps = 0;
            while (!sim.Finished && steps < 60 * 300)   // 300 simulated seconds at 60Hz
            {
                sim.Tick(1f / 60f);
                sim.Events.Clear();
                steps++;
            }

            Check(sim.Finished, $"battle reached a conclusion in {steps / 60f:F1}s");
            Check(sim.Won, $"a maxed party clears stage 1 (won={sim.Won})");
            Check(sim.GoldEarned > 0, $"gold awarded ({sim.GoldEarned})");

            // Every boss must be survivable to fight: run one wave of each phase's boss against a
            // maxed party and confirm it resolves rather than stalling (a self-healing boss that
            // out-heals the party would hang here, which is exactly the bug worth catching).
            for (var phase = 0; phase < GameData.Bosses.Count; phase++)
            {
                var stage = phase * 10 + 1;
                var name = GameData.BossForStage(stage)?.name;
                var s = new BattleSim(p, stage, waves: 1) { AutoSkill = true };
                var n = 0;
                while (!s.Finished && n < 60 * 120) { s.Tick(1f / 60f); s.Events.Clear(); n++; }
                Check(s.Finished, $"boss resolves: {name} (phase {phase + 1}, {n / 60f:F0}s)");
            }

            var weak = PlayerState.New();
            var d = GameData.Heroes.First(h => h.grade == "D");
            weak.owned.Add(new OwnedHero(d.id));
            weak.AddToParty(d.id);
            var hard = new BattleSim(weak, stage: 60) { AutoSkill = true };
            steps = 0;
            while (!hard.Finished && steps < 60 * 300) { hard.Tick(1f / 60f); hard.Events.Clear(); steps++; }
            Check(hard.Finished && !hard.Won, "one ★1 D card loses at stage 60 (the wall exists)");
        }
    }
}
