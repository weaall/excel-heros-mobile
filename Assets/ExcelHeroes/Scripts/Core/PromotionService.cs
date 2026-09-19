using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 승진 — the main hero's career, ported from the web build's `mainPromotionInfo` /
    /// `promoteMain` and the `MAIN_JOBS` table in `src/data/heroes.js`.
    ///
    /// 김인턴 is one person the whole way through: the same man with the same black bob, D grade at
    /// 인턴 and S grade at 부장. Promotion is not a purchase, it is a graduation — the web's own
    /// note — so it charges 강화 카드 AND asks that the tier be finished first.
    ///
    /// Right after 사원 the track forks three ways and never rejoins: 영업 is melee single-target
    /// and kills bosses, 재무 is ranged AoE with gold on top, 총무 tanks. The choice cannot be
    /// undone, which is why the confirm spells out what is being given up.
    ///
    /// ONE CONDITION IS DROPPED, on purpose. The web also requires the tier's 강화 한계 to be
    /// filled, where 강화 is a per-hero card upgrade separate from levelling. This build has no
    /// such concept — a hero's cap comes from ★ alone — so there is nothing to check and faking
    /// one would invent a currency. The web's note says the point of that condition is "finish this
    /// tier before moving on", and MAIN_PROMOTE_LEVEL already says that in a unit this build has.
    /// </summary>
    public static class PromotionService
    {
        /// <summary>The job the main hero currently holds; 인턴 on a save that has never promoted.</summary>
        public static MainJobDef Job(PlayerState p)
        {
            var id = p?.mainJob;
            var job = string.IsNullOrEmpty(id) ? null : GameData.MainJob(id);
            return job ?? GameData.MainJob("intern") ?? GameData.MainJobs.FirstOrDefault();
        }

        public readonly struct Info
        {
            public readonly bool Maxed;
            public readonly int Cards, Stage, Level, Tier;
            public readonly bool HasCards, HasStage, HasLevel;
            public readonly List<MainJobDef> Options;

            public Info(bool maxed, int cards, int stage, int level, int tier,
                        bool hasCards, bool hasStage, bool hasLevel, List<MainJobDef> options)
            {
                Maxed = maxed; Cards = cards; Stage = stage; Level = level; Tier = tier;
                HasCards = hasCards; HasStage = hasStage; HasLevel = hasLevel; Options = options;
            }

            public bool Ok => !Maxed && HasCards && HasStage && HasLevel;
        }

        public static Info For(PlayerState p)
        {
            var job = Job(p);
            var none = new List<MainJobDef>();
            if (job == null || job.next == null || job.next.Length == 0)
                return new Info(true, 0, 0, 0, job?.tier ?? 0, false, false, false, none);

            var b = GameData.Balance;
            var cards = At(b.mainPromoteCards, job.tier);
            var stage = At(b.mainPromoteStage, job.tier);
            var level = At(b.mainPromoteLevel, job.tier);

            var me = p.Find(GameData.MainId);
            var lvl = me?.level ?? 0;

            var options = job.next.Select(GameData.MainJob).Where(j => j != null).ToList();
            return new Info(false, cards, stage, level, job.tier,
                            p.cards >= cards, p.maxCleared >= stage, lvl >= level, options);
        }

        static int At(int[] table, int tier) =>
            table == null || table.Length == 0 ? 0 : table[System.Math.Clamp(tier, 0, table.Length - 1)];

        /// <summary>
        /// Promote to one of the current job's next jobs. Charges the cards and nothing else —
        /// the level and the stage are gates, not costs, so they are not spent.
        /// </summary>
        public static bool Promote(PlayerState p, string jobId)
        {
            var info = For(p);
            if (!info.Ok || info.Options.All(o => o.id != jobId)) return false;

            p.cards -= info.Cards;
            p.mainJob = jobId;
            return true;
        }

        /// <summary>
        /// The main hero as a HeroDef, so everything that already takes one — stats, the card, the
        /// sprite, 편성, skins — works without knowing they are special. Their definition IS their
        /// job, which is why a promotion changes their grade, role, trait and skill at once.
        /// </summary>
        public static HeroDef AsHero(PlayerState p)
        {
            var job = Job(p);
            if (job == null) return null;
            return new HeroDef
            {
                id = job.id,
                name = job.name,
                grade = job.grade,
                role = job.role,
                trait = job.trait,
                skillType = job.skillType,
                skillPower = job.skillPower,
                skillName = job.skillName,
                nick = job.title,
                dept = TrackName(job.track),
                // 경영지원본부, which is what the web's divisionOf('main') resolves to. He counts
                // towards 부문 시너지 exactly like a recruited hero.
                division = "admin",
                gender = "M",       // 김인턴 is a man at every rank, and stays the same man.
            };
        }

        public static string TrackName(string track) => track switch
        {
            "sales" => "영업 트랙",
            "finance" => "재무 트랙",
            "admin" => "총무 트랙",
            _ => "미배정",
        };

        /// <summary>What a track is for, shown on the fork that cannot be taken back.</summary>
        public static string TrackDesc(string track) => track switch
        {
            "sales" => "근접 단일 딜. 치명타와 팀 리더십으로 보스를 빠르게 잡습니다.",
            "finance" => "원거리 광역 딜. 골드 보너스와 보스 피해로 사냥 효율을 올립니다.",
            "admin" => "탱커. 방패와 전체 기절로 파티를 지킵니다.",
            _ => "",
        };

        /// <summary>The line the 승진 panel leads with — or "" when there is nothing to say.</summary>
        public static string Advice(PlayerState p)
        {
            var info = For(p);
            if (info.Maxed) return "";
            if (info.Ok) return "승진할 수 있습니다";

            // The one case worth interrupting for: the main hero is stuck at their level cap and
            // cannot go further without promoting, which in this build stalls the whole party.
            var me = p.Find(GameData.MainId);
            if (me != null && StatMath.AtLevelCap(me)) return "레벨 상한 · 승진이 필요합니다";
            return "";
        }
    }
}
