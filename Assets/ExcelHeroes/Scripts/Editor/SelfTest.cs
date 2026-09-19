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

        [MenuItem("Excel Heroes/Run Self Test")]
        public static void Run()
        {
            _failures = 0;
            Log.Clear();
            Line("=== Excel Heroes self test ===");

            GameData.Load();
            CheckData();
            CheckGachaRates();
            CheckPity();
            CheckStats();
            CheckSynergy();
            CheckBattle();

            Line(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
            if (_failures == 0) Debug.Log(Log.ToString());
            else Debug.LogError(Log.ToString());
        }

        static void Line(string s) => Log.AppendLine(s);

        static void Check(bool ok, string what)
        {
            if (!ok) _failures++;
            Line($"  [{(ok ? "ok" : "FAIL")}] {what}");
        }

        static void CheckData()
        {
            Line("\n-- data --");
            Check(GameData.Heroes.Count > 0, $"roster loaded ({GameData.Heroes.Count} heroes)");
            Check(GameData.Grades.Count == 5, $"5 grades ({string.Join(",", GameData.Grades.Select(g => g.id))})");
            Check(GameData.Roles.Count == 4, $"4 roles");
            Check(GameData.Episodes.Count > 0, $"{GameData.Episodes.Count} story episodes");
            Check(GameData.Balance != null && GameData.Balance.partySize > 0, "balance loaded");

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
