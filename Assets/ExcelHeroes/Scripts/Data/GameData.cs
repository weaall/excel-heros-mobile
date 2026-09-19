using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ExcelHeroes.Data
{
    /// <summary>
    /// The read-only game database, loaded once from Resources/Data at startup.
    /// Everything here comes from the web build via tools/export-data.mjs — the web repo stays the
    /// single source of truth for balance, so never hand-edit the JSON in this project.
    /// </summary>
    public static class GameData
    {
        public static bool Loaded { get; private set; }

        public static List<GradeDef> Grades { get; private set; } = new();
        public static List<RoleDef> Roles { get; private set; } = new();
        public static List<SkillDef> Skills { get; private set; } = new();
        public static List<TraitDef> Traits { get; private set; } = new();
        public static List<HeroDef> Heroes { get; private set; } = new();
        public static List<DivisionDef> Divisions { get; private set; } = new();
        public static List<PerkDef> Perks { get; private set; } = new();
        public static List<EpisodeDef> Episodes { get; private set; } = new();
        public static List<MainJobDef> MainJobs { get; private set; } = new();
        public static List<MonsterTypeDef> MonsterTypes { get; private set; } = new();
        public static List<BossDef> Bosses { get; private set; } = new();
        public static BalanceDef Balance { get; private set; }
        public static DivisionFile Synergy { get; private set; }
        public static string MainId { get; private set; } = "main";

        static Dictionary<string, GradeDef> _grades;
        static Dictionary<string, RoleDef> _roles;
        static Dictionary<string, SkillDef> _skills;
        static Dictionary<string, TraitDef> _traits;
        static Dictionary<string, HeroDef> _heroes;
        static Dictionary<string, DivisionDef> _divisions;
        static Dictionary<string, PerkDef> _perks;

        /// <summary>Grade ids weakest to strongest. Used for pity checks and sort order.</summary>
        public static readonly string[] GradeOrder = { "D", "C", "B", "A", "S" };

        public static void Load()
        {
            if (Loaded) return;

            Grades = Read<Wrapper<GradeDef>>("grades").items;
            Roles = Read<Wrapper<RoleDef>>("roles").items;
            Skills = Read<Wrapper<SkillDef>>("skills").items;
            Traits = Read<Wrapper<TraitDef>>("traits").items;
            Heroes = Read<Wrapper<HeroDef>>("heroes").items;
            Episodes = Read<Wrapper<EpisodeDef>>("story").items;
            Balance = Read<BalanceDef>("balance");

            Synergy = Read<DivisionFile>("divisions");
            Divisions = Synergy.items;
            Perks = Synergy.perks;

            var bestiary = Read<MonsterFile>("monsters");
            MonsterTypes = bestiary.items;
            Bosses = bestiary.bosses;

            var main = Read<MainJobFile>("mainJobs");
            MainJobs = main.items;
            MainId = string.IsNullOrEmpty(main.mainId) ? "main" : main.mainId;

            _grades = Grades.ToDictionary(g => g.id);
            _roles = Roles.ToDictionary(r => r.id);
            _skills = Skills.ToDictionary(s => s.id);
            _traits = Traits.ToDictionary(t => t.id);
            _heroes = Heroes.ToDictionary(h => h.id);
            _divisions = Divisions.ToDictionary(d => d.id);
            _perks = Perks.ToDictionary(p => p.id);

            Loaded = true;
            Debug.Log($"[GameData] {Heroes.Count} heroes, {Episodes.Count} episodes, " +
                      $"rates {string.Join(" ", Grades.Select(g => $"{g.id}={g.rate:P1}"))}");
        }

        static T Read<T>(string file)
        {
            var asset = Resources.Load<TextAsset>($"Data/{file}");
            if (asset == null) { Debug.LogError($"[GameData] missing Resources/Data/{file}.json"); return default; }
            return JsonUtility.FromJson<T>(asset.text);
        }

        public static GradeDef Grade(string id) => id != null && _grades.TryGetValue(id, out var v) ? v : null;
        public static RoleDef Role(string id) => id != null && _roles.TryGetValue(id, out var v) ? v : null;
        public static SkillDef Skill(string id) => id != null && _skills.TryGetValue(id, out var v) ? v : null;
        public static TraitDef Trait(string id) => id != null && _traits.TryGetValue(id, out var v) ? v : null;
        public static HeroDef Hero(string id) => id != null && _heroes.TryGetValue(id, out var v) ? v : null;
        public static DivisionDef Division(string id) => id != null && _divisions.TryGetValue(id, out var v) ? v : null;
        public static PerkDef Perk(string divisionId) => divisionId != null && _perks.TryGetValue(divisionId, out var v) ? v : null;

        public static int GradeRank(string gradeId) => System.Array.IndexOf(GradeOrder, gradeId);

        /// <summary>Ten stages to a phase, and each phase gets its own look and its own boss.</summary>
        public static int PhaseOf(int stage) => (Mathf.Max(1, stage) - 1) / 10;

        public static BossDef BossForStage(int stage) =>
            Bosses.Count == 0 ? null : Bosses[PhaseOf(stage) % Bosses.Count];

        /// <summary>
        /// The three enemy types a phase draws from, so the wave line-up visibly changes as the
        /// player pushes deeper instead of showing the same four errors forever.
        /// </summary>
        public static MonsterTypeDef MonsterForStage(int stage, int slot)
        {
            if (MonsterTypes.Count == 0) return null;
            var i = (PhaseOf(stage) * 3 + Mathf.Abs(slot) % 3) % MonsterTypes.Count;
            return MonsterTypes[i];
        }

        public static IEnumerable<HeroDef> OfGrade(string gradeId) => Heroes.Where(h => h.grade == gradeId);

        /// <summary>Card art lives at Resources/Art/Cards/&lt;id&gt;.png; falls back to a neutral placeholder.</summary>
        public static Sprite CardArt(string heroId)
        {
            var s = Resources.Load<Sprite>($"Art/Cards/{heroId}");
            return s != null ? s : Resources.Load<Sprite>("Art/Cards/_placeholder");
        }

        /// <summary>The extra frames used to make a card breathe/blink on the detail screen. May be empty.</summary>
        public static List<Sprite> CardMotion(string heroId)
        {
            var list = new List<Sprite>();
            foreach (var pose in new[] { "breathe", "blink", "talk", "smile" })
            {
                var s = Resources.Load<Sprite>($"Art/Cards/{heroId}__{pose}");
                if (s != null) list.Add(s);
            }
            return list;
        }

        /// <summary>Fills {p} in a skill description with the hero's own power value.</summary>
        public static string SkillText(HeroDef h)
        {
            var def = Skill(h.skillType);
            if (def == null) return h.skillName ?? "";
            var p = h.skillPower % 1f == 0f ? h.skillPower.ToString("0") : h.skillPower.ToString("0.#");
            return def.desc.Replace("{p}", p);
        }
    }
}
