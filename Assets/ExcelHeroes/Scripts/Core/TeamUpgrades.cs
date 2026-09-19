using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;
using UnityEngine;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 사무실 개선 — the office upgrades, ported from the web build's BALANCE.TEAM_UPGRADES.
    ///
    /// The auto-battle pays out gold whether or not anyone is watching it, and before this there
    /// was nothing to do with that gold except level one card at a time from inside a modal. These
    /// are the idle half of the loop: four permanent, party-wide bonuses on a geometric price curve,
    /// bought from a strip under the battlefield without leaving the fight.
    ///
    /// Deliberately mild per level and capped — 매출 인센티브 is +1%/Lv to 50, so it stretches the
    /// curve by half rather than replacing it. The stage ladder in SelfTest.Bench is what these have
    /// to stay inside.
    /// </summary>
    public static class TeamUpgrades
    {
        public static IReadOnlyList<TeamUpgradeDef> All =>
            GameData.Balance?.teamUpgrades ?? (IReadOnlyList<TeamUpgradeDef>)System.Array.Empty<TeamUpgradeDef>();

        public static TeamUpgradeDef Def(string id) => All.FirstOrDefault(t => t.id == id);

        public static int Level(PlayerState p, string id) => p?.TeamLevel(id) ?? 0;

        /// <summary>base * growth^level — the same formula as teamUpgradeCost in the web build.</summary>
        public static int Cost(PlayerState p, string id)
        {
            var def = Def(id);
            if (def == null) return int.MaxValue;
            return Mathf.FloorToInt(def.baseCost * Mathf.Pow(def.growth, Level(p, id)));
        }

        public static bool AtMax(PlayerState p, string id)
        {
            var def = Def(id);
            return def != null && Level(p, id) >= def.max;
        }

        public static bool CanBuy(PlayerState p, string id) =>
            p != null && Def(id) != null && !AtMax(p, id) && p.gold >= Cost(p, id);

        public static bool Buy(PlayerState p, string id)
        {
            if (!CanBuy(p, id)) return false;
            p.gold -= Cost(p, id);
            p.SetTeamLevel(id, Level(p, id) + 1);
            return true;
        }

        /// <summary>The total bonus this upgrade is currently granting, e.g. 0.12 for +12%.</summary>
        public static float Bonus(PlayerState p, string id)
        {
            var def = Def(id);
            if (def == null) return 0f;
            return def.per * Mathf.Min(Level(p, id), def.max);
        }

        // Named accessors, so the battle does not have to know the string keys.
        public static float AttackSpeed(PlayerState p) => Bonus(p, "coffee");
        public static float Health(PlayerState p) => Bonus(p, "chairs");
        public static float Gold(PlayerState p) => Bonus(p, "sales");
        public static float GemChance(PlayerState p) => Bonus(p, "payroll");

        /// <summary>
        /// 자동 구매 — the cheapest affordable upgrade, bought as many times as the gold allows.
        /// This is what the strip's ⟳ button runs, and it is the same "cheapest first" rule the web
        /// build's 자동 합계 uses for levelling: spreading the spend is what keeps the party even.
        /// </summary>
        public static int BuyCheapest(PlayerState p, int limit = 50)
        {
            var bought = 0;
            for (var i = 0; i < limit; i++)
            {
                string best = null;
                var bestCost = int.MaxValue;
                foreach (var def in All)
                {
                    if (AtMax(p, def.id)) continue;
                    var cost = Cost(p, def.id);
                    if (cost > p.gold || cost >= bestCost) continue;
                    best = def.id;
                    bestCost = cost;
                }
                if (best == null) break;
                Buy(p, best);
                bought++;
            }
            return bought;
        }
    }
}
