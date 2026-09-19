using System.Collections.Generic;
using System.Linq;
using ExcelHeroes.Data;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 비품 자동 장착 — ported from the web build's `bestLoadout` / `autoEquip` / `autoEquipParty`.
    ///
    /// Four slots times however many pieces are in the bag is more comparing than anyone will do by
    /// hand, and the right answer is not "the best piece in each slot": a full set of one grade pays
    /// a bonus that can beat four better mismatched pieces. So both are worked out and the higher
    /// total wins.
    ///
    /// The party is equipped in slot order, front first, because a piece can only be worn by one
    /// hero: whoever is earlier in the line-up gets first pick of the bag.
    /// </summary>
    public static class AutoEquipService
    {
        /// <summary>
        /// The best set of items this hero could be wearing, by slot. Items worn by ANOTHER hero
        /// are off the table — this never undresses someone else to dress this one, because a
        /// button that silently strips a team-mate is worse than one that does nothing.
        /// </summary>
        public static Dictionary<string, int> Best(PlayerState p, string heroId)
        {
            var b = GameData.Balance;
            var slots = EquipService.Slots.Select(s => s.id).ToList();

            // Anything on someone else's desk. `WornBy` is the existing answer to "who has this",
            // and the save keeps worn items as parallel key/id lists rather than as records, so
            // asking per item is both simpler and the only shape that stays right if that changes.
            var pool = p.items.Where(it =>
            {
                var holder = p.WornBy(it.id);
                return string.IsNullOrEmpty(holder) || holder == heroId;
            }).ToList();

            EquipItem BestIn(string slot, IEnumerable<EquipItem> from) =>
                from.Where(it => it.slot == slot).OrderByDescending(EquipService.Pct).FirstOrDefault();

            // Option one: the strongest piece in each slot, whatever grades they happen to be.
            var best = new Dictionary<string, int>();
            var bestSum = 0f;
            foreach (var slot in slots)
            {
                var it = BestIn(slot, pool);
                if (it == null) continue;
                best[slot] = it.id;
                bestSum += EquipService.Pct(it);
            }
            if (best.Count == slots.Count) bestSum += b.equipSetAny;

            // Option two: a complete set of one grade, which pays more the higher the grade.
            Dictionary<string, int> setBest = null;
            var setSum = -1f;
            for (var gi = 0; gi < b.equipSetSameGrades.Count; gi++)
            {
                var grade = b.equipSetSameGrades[gi];
                var picks = new Dictionary<string, int>();
                var sum = 0f;
                var complete = true;
                foreach (var slot in slots)
                {
                    var it = BestIn(slot, pool.Where(x => x.grade == grade));
                    if (it == null) { complete = false; break; }
                    picks[slot] = it.id;
                    sum += EquipService.Pct(it);
                }
                if (!complete) continue;
                sum += b.equipSetSameValues[gi];
                if (sum > setSum) { setSum = sum; setBest = picks; }
            }

            return setBest != null && setSum > bestSum ? setBest : best;
        }

        /// <summary>Dresses one hero. Returns how many slots actually changed.</summary>
        public static int Equip(PlayerState p, string heroId)
        {
            if (p.Find(heroId) == null) return 0;

            var want = Best(p, heroId);
            var changed = 0;
            foreach (var slot in EquipService.Slots.Select(s => s.id))
            {
                var now = EquipService.Worn(p, heroId, slot)?.id ?? 0;
                var next = want.TryGetValue(slot, out var id) ? id : 0;
                if (now == next) continue;

                if (next > 0) EquipService.Equip(p, heroId, next);
                else EquipService.Unequip(p, heroId, slot);
                changed++;
            }
            return changed;
        }

        /// <summary>
        /// Dresses the whole party in party order. The front of the line-up gets first pick, which
        /// is what makes the result depend on the order and why it is worth saying out loud.
        /// </summary>
        public static (int Heroes, int Slots) EquipParty(PlayerState p)
        {
            var heroes = 0;
            var slots = 0;
            foreach (var id in p.party)
            {
                if (string.IsNullOrEmpty(id)) continue;
                var n = Equip(p, id);
                if (n <= 0) continue;
                heroes++;
                slots += n;
            }
            return (heroes, slots);
        }
    }
}
