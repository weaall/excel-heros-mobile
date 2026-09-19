using System;
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
        public static PickupFile Pickup { get; private set; }
        public static QuestFile Quests { get; private set; }

        static Dictionary<string, AffectionText> _affection = new();
        static Dictionary<string, QuestDef> _quests = new();
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

            Pickup = Read<PickupFile>("pickup");
            Quests = Read<QuestFile>("quests");
            _quests = Quests.items.ToDictionary(q => q.id);
            _affection = Read<Wrapper<AffectionText>>("affection").items.ToDictionary(a => a.id);

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

        public static QuestDef Quest(string id) =>
            id != null && _quests.TryGetValue(id, out var v) ? v : null;

        public static AffectionText Affection(string heroId) =>
            heroId != null && _affection.TryGetValue(heroId, out var v) ? v : null;

        /// <summary>
        /// Which banner index a date falls in. Days since the epoch divided by the banner length —
        /// the same arithmetic the web build uses, so both clients feature the same hero on a date.
        /// </summary>
        public static int BannerIndex(DateTime dateUtc)
        {
            var days = (int)Math.Floor((dateUtc.Date - new DateTime(1970, 1, 1)).TotalDays);
            var span = Math.Max(1, Pickup?.days ?? 3);
            return (int)Math.Floor(days / (double)span);
        }

        /// <summary>Whole days left on the banner running on that date (1 = last day).</summary>
        public static int BannerDaysLeft(DateTime dateUtc)
        {
            var days = (int)Math.Floor((dateUtc.Date - new DateTime(1970, 1, 1)).TotalDays);
            var span = Math.Max(1, Pickup?.days ?? 3);
            return span - ((days % span) + span) % span;
        }

        /// <summary>The featured hero of a grade for the banner running on that date, or null.</summary>
        public static HeroDef Featured(string grade, DateTime dateUtc)
        {
            var order = Pickup?.orders?.FirstOrDefault(o => o.grade == grade);
            if (order == null || order.ids.Count == 0) return null;
            var i = BannerIndex(dateUtc) % order.ids.Count;
            if (i < 0) i += order.ids.Count;
            return Hero(order.ids[i]);
        }

        public static int SparkCost(string grade) =>
            grade == "S" ? Pickup?.sparkS ?? 150 : Pickup?.sparkA ?? 60;

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

        /// <summary>
        /// Card art lives at Resources/Art/Cards/&lt;id&gt;.png, with the two alternate outfits at
        /// &lt;id&gt;__casual and &lt;id&gt;__formal. These are the web build's own illustrations, half
        /// size — nothing here is generated, because generated replacements lost the likeness.
        /// </summary>
        public static Sprite CardArt(string heroId, string skin = null)
        {
            var key = string.IsNullOrEmpty(skin) ? heroId : $"{heroId}__{skin}";
            var s = Resources.Load<Sprite>($"Art/Cards/{key}");
            if (s == null && !string.IsNullOrEmpty(skin)) s = Resources.Load<Sprite>($"Art/Cards/{heroId}");
            return s;
        }

        public static readonly string[] Skins = { "", "casual", "formal" };

        /// <summary>The outfits this hero actually has art for, base first.</summary>
        public static List<string> SkinsOf(string heroId)
        {
            var list = new List<string>();
            foreach (var skin in Skins)
                if (Resources.Load<Sprite>(string.IsNullOrEmpty(skin) ? $"Art/Cards/{heroId}" : $"Art/Cards/{heroId}__{skin}") != null)
                    list.Add(skin);
            return list;
        }

        // The battle sprite is one 144x28 strip of nine 16x28 frames: idle 0-3, walk 4-7, hit 8.
        // That layout comes straight from the web build's sprite builder, and the frames are sliced
        // here rather than baked apart so the whole cast costs one texture per hero.
        public const int SpriteFrameW = 16, SpriteFrameH = 28, SpriteFrames = 9;
        static readonly Dictionary<string, Sprite[]> FrameCache = new();

        /// <summary>The hero's nine battle frames, or null if that hero has no sprite yet.</summary>
        public static Sprite[] BattleFrames(string heroId)
        {
            if (FrameCache.TryGetValue(heroId, out var cached)) return cached;

            var strip = Resources.Load<Sprite>($"Art/Sprites/{heroId}");
            if (strip == null) { FrameCache[heroId] = null; return null; }

            var tex = strip.texture;
            var frames = new Sprite[SpriteFrames];
            for (var i = 0; i < SpriteFrames; i++)
                frames[i] = Sprite.Create(tex, new Rect(i * SpriteFrameW, 0, SpriteFrameW, SpriteFrameH),
                                          new Vector2(0.5f, 0f), SpriteFrameH);
            FrameCache[heroId] = frames;
            return frames;
        }

        /// <summary>Frame 0 — the standing pose, for anywhere a small hero icon is wanted.</summary>
        public static Sprite BattleSprite(string heroId) => BattleFrames(heroId)?[0];

        // Monster strips are four idle frames wide, already mirrored to face the party and scaled
        // by the web build's own cutter — but each creature has its own frame size, so unlike the
        // heroes the width is read off the texture rather than assumed.
        const int MonsterFrameCount = 4;
        static readonly Dictionary<string, Sprite[]> MonsterCache = new();

        public static Sprite[] MonsterFrames(string typeId)
        {
            if (string.IsNullOrEmpty(typeId)) return null;
            if (MonsterCache.TryGetValue(typeId, out var cached)) return cached;

            var strip = Resources.Load<Sprite>($"Art/Monsters/{typeId}");
            if (strip == null) { MonsterCache[typeId] = null; return null; }

            var tex = strip.texture;
            var w = tex.width / MonsterFrameCount;
            var frames = new Sprite[MonsterFrameCount];
            for (var i = 0; i < MonsterFrameCount; i++)
                frames[i] = Sprite.Create(tex, new Rect(i * w, 0, w, tex.height), new Vector2(0.5f, 0f), tex.height);
            MonsterCache[typeId] = frames;
            return frames;
        }

        public static Sprite MonsterSprite(string typeId) => MonsterFrames(typeId)?[0];

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
