using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 자동 편성 — ported from the web build's `autoParty` and `partyScore`.
    ///
    /// Picking a party by hand means opening 55 cards and comparing five numbers on each, and the
    /// answer is not "the five biggest": 부문 시너지, a healer and a balanced spread of roles are
    /// all worth more than raw 전투력, and none of them is visible from a card on its own.
    ///
    /// The search is the web's: seed with the best tank and healer, fill on power, then hill-climb
    /// by swapping one slot at a time while the score improves. Three passes over a pool of 18 is
    /// enough to settle and stays instant; an exhaustive search over 55 choose 4 is not worth it
    /// for a result that is the same nine times in ten.
    /// </summary>
    public static class AutoPartyService
    {
        /// <summary>
        /// What a party is worth: summed 전투력, multiplied by 부문 시너지, by whether it has a
        /// healer, and by whether the roles are balanced. Same shape as the web's `partyScore`.
        /// </summary>
        public static float Score(PlayerState p, IReadOnlyList<string> ids)
        {
            var before = p.party;
            p.party = ids.ToList();
            var syn = StatMath.Synergy(p);
            p.party = before;

            var power = 0f;
            var roles = new HashSet<string>();
            foreach (var id in ids)
            {
                var o = p.Find(id);
                if (o == null) continue;
                power += StatMath.Power(o);
                var role = GameData.Hero(id)?.role;
                if (!string.IsNullOrEmpty(role)) roles.Add(role);
            }

            return power
                 * (1f + syn.atkBonus + syn.hpBonus / 2f)
                 * (1f + syn.perks.Count * 0.04f)
                 * (roles.Contains("healer") ? 1.08f : 1f)
                 * (roles.Count >= 4 ? 1.06f : 1f);
        }

        /// <summary>
        /// Fills the party. 김인턴 always holds a slot — he is the player, not a pick — and the
        /// rest are chosen for the best score the roster can reach.
        ///
        /// `minGrade` is a preference and not a promise: if that grade alone cannot fill the party,
        /// the search falls back to the whole roster rather than fielding four people.
        /// </summary>
        public static List<string> Auto(PlayerState p, string minGrade = null)
        {
            var size = GameData.Balance?.partySize ?? 5;
            var slots = size - 1;                       // one is always the main hero

            var bench = p.owned
                .Where(o => o.id != GameData.MainId && !DispatchService.IsAway(p, o.id))
                .OrderByDescending(StatMath.Power)
                .ToList();

            var cut = string.IsNullOrEmpty(minGrade) ? -1 : GameData.GradeRank(minGrade);
            var limited = cut < 0
                ? bench
                : bench.Where(o => GameData.GradeRank(GameData.Hero(o.id)?.grade) >= cut).ToList();
            var pool = (limited.Count >= slots ? limited : bench).ToList();

            // Beyond the top 18 nothing can win a slot, and capping it keeps the search instant.
            var candidates = pool.Take(18).Select(o => o.id).ToList();

            // Seed: the best tank and the best healer first, because both are worth more than the
            // power they bring and neither will win a slot on power alone.
            var seed = new List<string>();
            foreach (var role in new[] { "tank", "healer" })
            {
                var best = pool.FirstOrDefault(o => GameData.Hero(o.id)?.role == role && !seed.Contains(o.id));
                if (best != null) seed.Add(best.id);
            }
            foreach (var o in pool)
            {
                if (seed.Count >= slots) break;
                if (!seed.Contains(o.id)) seed.Add(o.id);
            }

            // A role the roster can supply has to stay represented, whatever the raw numbers say.
            var required = new[] { "tank", "healer" }
                .Where(r => pool.Any(o => GameData.Hero(o.id)?.role == r))
                .ToList();
            bool Legal(List<string> ids) =>
                required.All(r => ids.Any(id => GameData.Hero(id)?.role == r));

            var team = new List<string> { GameData.MainId };
            team.AddRange(seed.Take(slots));
            while (team.Count < size) team.Add("");

            for (var pass = 0; pass < 3; pass++)
            {
                var improved = false;
                for (var i = 1; i < team.Count; i++)
                {
                    foreach (var cand in candidates)
                    {
                        if (team.Contains(cand)) continue;
                        var alt = team.ToList();
                        alt[i] = cand;
                        if (!Legal(alt)) continue;
                        if (Score(p, alt) > Score(p, team) + 1e-6f) { team = alt; improved = true; }
                    }
                }
                if (!improved) break;
            }

            p.party = team;
            return team;
        }
    }
}
